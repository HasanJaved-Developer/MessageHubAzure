using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedLibrary.Cache;
using SharedLibrary.MessageBus;
using System;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AzureFunctions.Invalidator
{
    public class RolePermissionsInvalidator
    {
        private readonly ILogger<RolePermissionsInvalidator> _logger;
        private readonly ICacheAccessProvider _cache;
        private readonly IHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private HttpClient _httpClient;

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        public RolePermissionsInvalidator(
            ILogger<RolePermissionsInvalidator> logger,            
            IHttpClientFactory httpClientFactory,
            IHostEnvironment environment,
            IConfiguration configuration)
        {
            _logger = logger;            
            _environment = environment;
            _httpClient = httpClientFactory.CreateClient("ApiIntegrationMvc");
            _configuration = configuration;
        }

        [Function(nameof(RolePermissionsInvalidator))]
        public async Task Run(
            [ServiceBusTrigger(
                "role-permissions-updated",
                Connection = "ServiceBusConnection")]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions messageActions,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation(
                "Function environment: {Environment}",
                _environment.EnvironmentName);                

                _logger.LogInformation(
                    "Processing message {MessageId}",
                    message.MessageId);
                
                var evt =
                    JsonSerializer.Deserialize<RolePermissionsUpdatedEvent>(
                        message.Body.ToString(),
                        JsonOptions);

                if (evt is null)
                {
                    throw new InvalidOperationException(
                        "Unable to deserialize RolePermissionsUpdatedEvent.");
                }

                #if DEBUG
                var secret = _configuration["ApiIntegrationMvc__ApiKey"];
                #else
                var secret = _configuration["ApiIntegrationMvc:ApiKey"];
                #endif
                var timestamp = DateTimeOffset.UtcNow.ToString("O");                
                var payload = $"{timestamp}:{message.Body.ToString()}";

                var key = Encoding.UTF8.GetBytes(secret);
                var data = Encoding.UTF8.GetBytes(payload);

                var signature = Convert.ToHexString(
                    HMACSHA256.HashData(key, data));
                
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login/InvalidateCacheApi");
                request.Headers.Add("X-Request-Timestamp", timestamp);
                request.Headers.Add("X-Request-Signature", signature);
                request.Content = new StringContent(message.Body.ToString(), Encoding.UTF8, "application/json");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                                

                await messageActions.CompleteMessageAsync(
                    message,
                    cancellationToken);
                                
                _logger.LogInformation(
                    "Invalidate Cache Api consumed successfully to invalidate permission cache keys for role {Role}",                    
                    evt.Role);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to process message {MessageId}",
                    message.MessageId);

                throw;
            }
        }
    }
}
