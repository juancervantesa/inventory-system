using IdentityModel.Client;
using System.Net.Http.Headers;

namespace Orders.Service.Handlers
{
    public class AuthenticationDelegatingHandler : DelegatingHandler
    {
        private readonly IConfiguration _configuration;
        private string _accessToken;
        private DateTime _accessTokenExpiration;

        public AuthenticationDelegatingHandler(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_accessToken == null || DateTime.UtcNow >= _accessTokenExpiration)
            {
                await RequestNewToken();
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            return await base.SendAsync(request, cancellationToken);
        }

        private async Task RequestNewToken()
        {
            var client = new HttpClient();
            var disco = await client.GetDiscoveryDocumentAsync(new DiscoveryDocumentRequest
            {
                Address = _configuration["Keycloak:Authority"],
                Policy = { RequireHttps = false } // Allow HTTP for development
            });

            if (disco.IsError)
            {
                throw new Exception(disco.Error);
            }

            var tokenResponse = await client.RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest
            {
                Address = disco.TokenEndpoint,
                ClientId = _configuration["Keycloak:ClientId"],
                ClientSecret = _configuration["Keycloak:ClientSecret"],
                Resource = { _configuration["Keycloak:ProductsAudience"] }
            });

            if (tokenResponse.IsError)
            {
                throw new Exception(tokenResponse.Error);
            }

            _accessToken = tokenResponse.AccessToken;
            _accessTokenExpiration = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 30); // Add a small buffer
        }
    }
}
