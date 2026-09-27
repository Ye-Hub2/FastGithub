using FastGithub.Configuration;
using FastGithub.DomainResolve;

namespace FastGithub.HttpServer.TcpMiddlewares
{
    /// <summary>
    /// github的git代理处理者
    /// </summary>
    sealed class GithubGitReverseProxyHandler : TcpReverseProxyHandler
    {
        /// <summary>
        /// github的git代理处理者
        /// </summary>
        /// <param name="domainResolver"></param>
        /// <param name="fastGithubConfig"></param>
        public GithubGitReverseProxyHandler(IDomainResolver domainResolver, FastGithubConfig fastGithubConfig)
            : base(domainResolver, new("github.com", 9418), fastGithubConfig)
        {
        }
    }
}
