using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedLibrary.Cache;
using SharedLibrary.MessageBus;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace AzureFunctions.Invalidator
{
    public class RolePermissionsInvalidator
    {
        private readonly ILogger<RolePermissionsInvalidator> _logger;
        private readonly ICacheAccessProvider _cache;
        private readonly IHostEnvironment _environment;

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        public RolePermissionsInvalidator(
            ILogger<RolePermissionsInvalidator> logger,
            ICacheAccessProvider cache,
            IHostEnvironment environment)
        {
            _logger = logger;
            _cache = cache;
            _environment = environment;
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

                var deleted = await _cache.InvalidatePermissionsForRoleAsync(
                    evt.Role,
                    cancellationToken);

                await messageActions.CompleteMessageAsync(
                    message,
                    cancellationToken);

                _logger.LogInformation(
                    "Deleted {Count} permission cache keys for role {Role}",
                    deleted,
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
