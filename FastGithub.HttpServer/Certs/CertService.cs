using FastGithub.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace FastGithub.HttpServer.Certs
{
    /// <summary>
    /// 证书服务
    /// </summary>
    sealed class CertService
    {
        private const string CACERT_PATH = "cacert";
        private readonly IMemoryCache serverCertCache;
        private readonly IEnumerable<ICaCertInstaller> certInstallers;
        private readonly IOptions<FastGithubOptions> options;
        private readonly ILogger<CertService> logger;
        private bool gitSslverifyConfigured;
        private X509Certificate2? caCert;


        /// <summary>
        /// 获取证书文件路径
        /// </summary>
        public string CaCerFilePath { get; } = OperatingSystem.IsLinux() ? $"{CACERT_PATH}/fastgithub.crt" : $"{CACERT_PATH}/fastgithub.cer";

        /// <summary>
        /// 获取私钥文件路径
        /// </summary>
        public string CaKeyFilePath { get; } = $"{CACERT_PATH}/fastgithub.key";

        /// <summary>
        /// 证书服务
        /// </summary>
        /// <param name="serverCertCache"></param>
        /// <param name="certInstallers"></param>
        /// <param name="options"></param>
        /// <param name="logger"></param>
        public CertService(
            IMemoryCache serverCertCache,
            IEnumerable<ICaCertInstaller> certInstallers,
            IOptions<FastGithubOptions> options,
            ILogger<CertService> logger)
        {
            this.serverCertCache = serverCertCache;
            this.certInstallers = certInstallers;
            this.options = options;
            this.logger = logger;
            Directory.CreateDirectory(CACERT_PATH);
        }

        /// <summary>
        /// 生成CA证书
        /// </summary> 
        public bool CreateCaCertIfNotExists()
        {
            if (File.Exists(this.CaCerFilePath) && File.Exists(this.CaKeyFilePath))
            {
                return false;
            }

            File.Delete(this.CaCerFilePath);
            File.Delete(this.CaKeyFilePath);

            var notBefore = DateTimeOffset.Now.AddDays(-1);
            var notAfter = DateTimeOffset.Now.AddYears(10);

            var subjectName = new X500DistinguishedName($"CN={nameof(FastGithub)}");
            this.caCert = CertGenerator.CreateCACertificate(subjectName, notBefore, notAfter);

            var privateKeyPem = this.caCert.GetRSAPrivateKey()?.ExportRSAPrivateKeyPem();
            File.WriteAllText(this.CaKeyFilePath, new string(privateKeyPem), Encoding.ASCII);

            var certPem = this.caCert.ExportCertificatePem();
            File.WriteAllText(this.CaCerFilePath, new string(certPem), Encoding.ASCII);

            return true;
        }

        /// <summary>
        /// 安装和信任CA证书
        /// </summary> 
        public void InstallAndTrustCaCert()
        {
            var installer = this.certInstallers.FirstOrDefault(item => item.IsSupported());
            if (installer != null)
            {
                installer.Install(this.CaCerFilePath);
            }
            else
            {
                this.logger.LogWarning($"请根据你的系统平台手动安装和信任CA证书{this.CaCerFilePath}");
            }

            this.ConfigureGitSslverify();
        }

        /// <summary>
        /// 配置git的ssl校验
        /// 默认不修改git的任何配置，仅在DisableGitSslverify为true时对已配置的具体域名关闭校验
        /// </summary>
        private void ConfigureGitSslverify()
        {
            if (this.gitSslverifyConfigured == true)
            {
                return;
            }
            this.gitSslverifyConfigured = true;

            this.CheckGlobalGitSslverify();

            if (this.options.Value.DisableGitSslverify == false)
            {
                this.logger.LogInformation($"如果git提示SSL certificate problem，请执行git config --global http.sslBackend schannel，或将CA证书{this.CaCerFilePath}加入git的信任列表");
                return;
            }

            var domains = this.options.Value.DomainConfigs.Keys.Where(item => item.Contains('*') == false).ToArray();
            if (domains.Length == 0)
            {
                this.logger.LogWarning($"已开启{nameof(FastGithubOptions.DisableGitSslverify)}，但没有配置具体域名，未修改git配置，可手动执行git config --global http.sslverify false");
                return;
            }

            foreach (var domain in domains)
            {
                GitConfigSslverify(domain, false);
            }
            this.logger.LogInformation($"已为[{string.Join(", ", domains)}]关闭git的ssl校验");
        }

        /// <summary>
        /// 检测git全局配置中遗留的sslverify=false
        /// </summary>
        private void CheckGlobalGitSslverify()
        {
            if (GetGlobalGitSslverify() != false)
            {
                return;
            }

            this.logger.LogWarning($"检测到git全局配置http.sslverify=false（历史版本遗留），git将不再校验服务器证书。建议执行 git config --global --unset http.sslverify 恢复校验；如果环境不允许，可改为只对github关闭：git config --global http.https://github.com/.sslverify false");
        }

        /// <summary>
        /// 获取git全局配置http.sslverify的值
        /// </summary>
        /// <returns>未配置或无法获取时返回null</returns>
        private static bool? GetGlobalGitSslverify()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "config --global --get http.sslverify",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return null;
                }

                if (process.WaitForExit(3000) == false)
                {
                    process.Kill();
                    return null;
                }

                var output = process.StandardOutput.ReadToEnd().Trim();
                return ParseGitBoolean(output);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 解析git的布尔值
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool? ParseGitBoolean(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "true":
                case "yes":
                case "on":
                case "1":
                    return true;
                case "false":
                case "no":
                case "off":
                case "0":
                    return false;
                default:
                    return null;
            }
        }

        /// <summary>
        /// 设置指定域名的ssl验证
        /// </summary>
        /// <param name="domain">域名</param>
        /// <param name="value">是否验证</param>
        /// <returns></returns>
        public static bool GitConfigSslverify(string domain, bool value)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"config --global http.https://{domain}/.sslverify {value.ToString().ToLower()}",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 获取颁发给指定域名的证书
        /// </summary>
        /// <param name="domain"></param> 
        /// <returns></returns>
        public X509Certificate2 GetOrCreateServerCert(string? domain)
        {
            if (this.caCert == null)
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(File.ReadAllText(this.CaKeyFilePath));
                this.caCert = new X509Certificate2(this.CaCerFilePath).CopyWithPrivateKey(rsa);
            }

            var key = $"{nameof(CertService)}:{domain}";
            var endCert = this.serverCertCache.GetOrCreate(key, GetOrCreateCert);
            return endCert!;

            // 生成域名的1年证书
            X509Certificate2 GetOrCreateCert(ICacheEntry entry)
            {
                var notBefore = DateTimeOffset.Now.AddDays(-1);
                var notAfter = DateTimeOffset.Now.AddYears(1);
                entry.SetAbsoluteExpiration(notAfter);

                var extraDomains = GetExtraDomains();

                var subjectName = new X500DistinguishedName($"CN={domain}");
                var endCert = CertGenerator.CreateEndCertificate(this.caCert, subjectName, extraDomains, notBefore, notAfter);

                // 重新初始化证书，以兼容win平台不能使用内存证书
                return new X509Certificate2(endCert.Export(X509ContentType.Pfx));
            }
        }

        /// <summary>
        /// 获取域名
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        private static IEnumerable<string> GetExtraDomains()
        {
            yield return Environment.MachineName;
            yield return IPAddress.Loopback.ToString();
            yield return IPAddress.IPv6Loopback.ToString();
        }
    }
}
