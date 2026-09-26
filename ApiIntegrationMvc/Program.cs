using ApiIntegrationMvc;
using ApiIntegrationMvc.Middlewares;
using CentralizedLogging.Sdk.Extensions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using SharedLibrary;
using SharedLibrary.Auth;
using SharedLibrary.Auths;
using SharedLibrary.Cache;
using StackExchange.Redis;
using UserManagement.Sdk.Extensions;


var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.WithProperty("Application", "IntegrationPortal")
    .Enrich.FromLogContext()
    .Enrich.With(new ActivityTraceEnricher())  // <-- custom enricher
    .CreateLogger();

builder.Host.UseSerilog();

// Add IDistributedCache globally
var env = builder.Environment.EnvironmentName; 
builder.Services.AddRedisCacheSupport(builder.Configuration, $"{env}:");

builder.Services.AddHttpContextAccessor(); // required for the above

builder.Services.AddUserManagementSdk();
builder.Services.AddCentralizedLoggingSdk();
// Delegating handler MUST be transient
builder.Services.AddTransient<BearerTokenHandler>();
// Register the token provider (memory-based)
builder.Services.AddScoped<ICacheAccessProvider, CacheAccessProvider>();


// Add services to the container.
builder.Services.AddControllersWithViews();

// Cookie auth for the web app (UI)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.LogoutPath = "/Account/Logout";
        o.AccessDeniedPath = "/Home/Home/Denied";
        // optional:
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PolicyType.WEB_LEVEL, p =>
    {
        p.RequireAuthenticatedUser();
        p.Requirements.Add(new PermissionRequirement("Web"));
    });
});

// OpenTelemetry + Jaeger
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(
        serviceName: "IntegrationPortal", // <- change per project
        serviceVersion: "1.0.0"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation(o => { o.RecordException = true; })
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri($"http://{builder.Configuration["JAEGER_HOST"]}:4317"); // host -> docker            
            o.Protocol = OtlpExportProtocol.Grpc;
        }));

builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

var app = builder.Build();
app.UseSerilogRequestLogging(); // structured request logs

app.MapGet("/health", () => Results.Ok("OK"));

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<EnsurePermissionsCachedMiddleware>();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Login}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "root_to_login",
    pattern: "",
    defaults: new { area = "Account", controller = "Login", action = "Index" });

app.Run();
