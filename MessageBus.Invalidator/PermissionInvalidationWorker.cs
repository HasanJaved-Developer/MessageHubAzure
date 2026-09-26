using Microsoft.Extensions.Options;
using SharedLibrary.Cache;
using SharedLibrary.MessageBus;
using System.Text.Json;
using RabbitMQ.Client.Events; // for AsyncEventingBasicConsumer
using System.Text;
using RabbitMQ.Client;

namespace MessageBus.Invalidator
{
    public sealed class PermissionInvalidationWorker : BackgroundService
    {
        private readonly IRabbitMqConnection _connection;
        private readonly RabbitMqOptions _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private const string QueueName = "perm.invalidate";
        private const string RoutingKey = "role.permissions.updated";

        public PermissionInvalidationWorker(
            IRabbitMqConnection connection,
            IOptions<RabbitMqOptions> options,
            IServiceScopeFactory scopeFactory)
        {
            _connection = connection;
            _options = options.Value;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 1) Create a long-lived channel for this worker
            var channel = await _connection.CreateChannelAsync();

            // 2) Ensure exchange exists (same as API)
            await channel.ExchangeDeclareAsync(
                exchange: _options.Exchange,
                type: _options.ExchangeType,   // "direct"
                durable: _options.Durable,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken);

            // 3) Declare durable queue
            await channel.QueueDeclareAsync(
                queue: QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken);

            // 4) Bind queue to exchange with routing key
            await channel.QueueBindAsync(
                queue: QueueName,
                exchange: _options.Exchange,
                routingKey: RoutingKey,
                arguments: null,
                cancellationToken: stoppingToken);

            // 5) Create async consumer
            var consumer = new AsyncEventingBasicConsumer(channel);

            consumer.ReceivedAsync += async (sender, ea) =>
            {
                try
                {
                    // per-message scope
                    using var scope = _scopeFactory.CreateScope();

                    var cache = scope.ServiceProvider
                                     .GetRequiredService<ICacheAccessProvider>();

                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var evt = JsonSerializer.Deserialize<RolePermissionsUpdatedEvent>(json, JsonOptions);

                    if (evt is not null)
                    {
                        // Call your cache invalidation logic
                        await cache.InvalidatePermissionsForRoleAsync(evt.Role, stoppingToken);
                    }

                    // Acknowledge the message as processed
                    await channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    // log error if you like
                    // Requeue the message so it isn't lost
                    await channel.BasicNackAsync(
                        deliveryTag: ea.DeliveryTag,
                        multiple: false,
                        requeue: true);
                }
            };

            // 6) Start consuming
            await channel.BasicConsumeAsync(
                queue: QueueName,
                autoAck: false,       // we manually ack after invalidation
                consumer: consumer,
                cancellationToken: stoppingToken);

            // Keep the worker alive
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
    }
}
