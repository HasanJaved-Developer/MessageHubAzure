using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedLibrary.Cache;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Application Insights isn't enabled by default. See https://aka.ms/AAt8mw4.
// builder.Services
//     .AddApplicationInsightsTelemetryWorkerService()
//     .ConfigureFunctionsApplicationInsights();

// CacheAccessProvider constructor currently requires IHttpContextAccessor.
// The Service Bus-triggered function itself has no HttpContext.
builder.Services.AddHttpContextAccessor();

// Register Dragonfly / Redis
var env = builder.Environment.EnvironmentName;

builder.Services.AddRedisCacheSupport(
    builder.Configuration,
    $"{env}:");

// Register shared cache provider
builder.Services.AddScoped<ICacheAccessProvider, CacheAccessProvider>();


builder.Build().Run();
