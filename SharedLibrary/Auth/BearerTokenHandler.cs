using SharedLibrary.Cache;
using System.Net.Http.Headers;

namespace SharedLibrary.Auths
{
    // IMPORTANT: register this as Transient in DI.
    // It asks the IAccessTokenProvider for the current token each request.
    public sealed class BearerTokenHandler : DelegatingHandler
    {
        private readonly ICacheAccessProvider _tokenProvider;

        public BearerTokenHandler(ICacheAccessProvider tokenProvider)
            => _tokenProvider = tokenProvider;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
