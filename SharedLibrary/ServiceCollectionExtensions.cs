using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRedisCacheSupport(this IServiceCollection services, IConfiguration configuration, string instanceName)
    {
        // 1) For IDistributedCache (tokens, simple JSON blobs)
        services.AddStackExchangeRedisCache(options =>
        {
            options.ConfigurationOptions = new ConfigurationOptions
            {
                EndPoints = { configuration["Redis:Endpoint"]! },
                Password = configuration["Redis:Password"],
                AbortOnConnectFail = false
            };

            // prefix for cache keys
            options.InstanceName = instanceName;
        });

        // 2) For Redis structures (SADD/SMEMBERS/etc.)
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(new ConfigurationOptions
            {
                EndPoints = { configuration["Redis:Endpoint"]! },
                Password = configuration["Redis:Password"],
                AbortOnConnectFail = false
            }));

        services.AddSingleton<IDatabase>(sp =>
            sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

        return services;
    }
}