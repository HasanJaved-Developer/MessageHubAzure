using MessageBus.Invalidator;
using SharedLibrary.Cache;
using SharedLibrary.MessageBus;

var builder = Host.CreateApplicationBuilder(args);

// Needed because CacheAccessProvider depends on it
builder.Services.AddHttpContextAccessor();

// 1. Bind RabbitMQ options from appsettings.json
builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMq"));

// 2. Register RabbitMQ connection (singleton)
builder.Services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();

// 3. Register Cache Invalidator (your Dragonfly/Redis logic)
var env = builder.Environment.EnvironmentName;
builder.Services.AddRedisCacheSupport(builder.Configuration, $"{env}:");
builder.Services.AddScoped<ICacheAccessProvider, CacheAccessProvider>();

// 4. Register the background worker (queue consumer)
builder.Services.AddHostedService<PermissionInvalidationWorker>();

var host = builder.Build();
host.Run();
