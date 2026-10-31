#if !DEBUG
using Microsoft.Azure.Functions.Worker;
#endif
using Microsoft.Azure.Functions.Worker.Builder;
using SharedLibrary.Cache;

var builder = FunctionsApplication.CreateBuilder(args);

// Application Insights
#if !DEBUG
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();
#endif

// Register shared cache provider
builder.Services.AddHttpClient("ApiIntegrationMvc", client =>
{
    var baseAddress = builder.Configuration[
        "ApiIntegrationMvc:BaseAddress"];

    if (string.IsNullOrWhiteSpace(baseAddress))
        throw new InvalidOperationException(
            "ApiIntegrationMvc:BaseAddress is missing.");

    client.BaseAddress = new Uri(baseAddress);
});

builder.Build().Run();
