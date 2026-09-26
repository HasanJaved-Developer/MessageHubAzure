using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace SharedLibrary.MessageBus

{
    public sealed class PermissionsEventPublisher : IPermissionsEventPublisher
    {
        private readonly IRabbitMqConnection _connection;
        private readonly RabbitMqOptions _options;

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public PermissionsEventPublisher(
            IRabbitMqConnection connection,
            IOptions<RabbitMqOptions> options)
        {
            _connection = connection;
            _options = options.Value;
        }

        public async Task PublishRolePermissionsUpdatedAsync(RolePermissionsUpdatedEvent evt, CancellationToken ct = default)
        {  

            var bodyBytes = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(evt, JsonOptions));

            await using var channel = await _connection.CreateChannelAsync();

            await channel.ExchangeDeclareAsync(
                exchange: _options.Exchange,
                type: _options.ExchangeType,
                durable: _options.Durable,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct);

            var props = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent
            };

            const string routingKey = "role.permissions.updated";

            await channel.BasicPublishAsync(
                exchange: _options.Exchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: props,
                body: bodyBytes,
                cancellationToken: ct);
        }
    }
}
