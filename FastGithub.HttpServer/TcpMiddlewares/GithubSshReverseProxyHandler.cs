using FastGithub.Configuration;
using FastGithub.DomainResolve;

namespace FastGithub.HttpServer.TcpMiddlewares
{
    /// <summary>
    /// github的ssh代理处理者
    /// </summary>
    sealed class GithubSshReverseProxyHandler : TcpReverseProxyHandler
    {
        /// <summary>
        /// github的ssh代理处理者
        /// </summary>
        /// <param name="domainResolver"></param>
        /// <param name="fastGithubConfig"></param>
        public GithubSshReverseProxyHandler(IDomainResolver domainResolver, FastGithubConfig fastGithubConfig)
            : base(domainResolver, new("github.com", 22), fastGithubConfig)
        {
        }
    }
}
