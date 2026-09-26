using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace SharedLibrary.MessageBus
{ 
    public interface IRabbitMqConnection : IAsyncDisposable
    {
        Task<IChannel> CreateChannelAsync();
    }

    public sealed class RabbitMqConnection : IRabbitMqConnection
    {
        private readonly ConnectionFactory _factory;
        private readonly Task<IConnection> _connectionTask;

        public RabbitMqConnection(IOptions<RabbitMqOptions> options)
        {
            var cfg = options.Value;

            _factory = new ConnectionFactory
            {
                HostName = cfg.HostName,
                Port = cfg.Port,
                UserName = cfg.UserName,
                Password = cfg.Password                
            };

            // Start connecting immediately (async, non-blocking)
            _connectionTask = _factory.CreateConnectionAsync();
        }

        public async Task<IChannel> CreateChannelAsync()
        {
            var conn = await _connectionTask.ConfigureAwait(false);            
            return await conn.CreateChannelAsync().ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_connectionTask.IsCompletedSuccessfully)
                {
                    var conn = await _connectionTask.ConfigureAwait(false);
                    await conn.CloseAsync().ConfigureAwait(false);
                    await conn.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // swallow on dispose; log if you like
            }
        }
    }
}
