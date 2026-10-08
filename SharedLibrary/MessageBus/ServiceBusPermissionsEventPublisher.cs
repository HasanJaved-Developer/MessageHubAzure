using Microsoft.Extensions.Options;
using Azure.Messaging.ServiceBus;
using System.Text.Json;


namespace SharedLibrary.MessageBus
{
    public sealed class ServiceBusPermissionsEventPublisher : IPermissionsEventPublisher
    {
        private readonly ServiceBusSender _sender;

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        public ServiceBusPermissionsEventPublisher(
            ServiceBusClient client,
            IOptions<ServiceBusOptions> options)
        {
            _sender = client.CreateSender(
                options.Value.RolePermissionsUpdatedQueue);
        }

        public async Task PublishRolePermissionsUpdatedAsync(
            RolePermissionsUpdatedEvent evt,
            CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(evt, JsonOptions);

            var message = new ServiceBusMessage(json)
            {
                ContentType = "application/json",
                Subject = "role.permissions.updated"
            };

            await _sender.SendMessageAsync(message, ct);
        }
    }

}
