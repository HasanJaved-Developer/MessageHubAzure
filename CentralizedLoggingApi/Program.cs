using CentralizedLoggingApi;
using CentralizedLoggingApi.Data;
using CentralizedLoggingApi.DTO.Auth;
using CentralizedLoggingApi.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using SharedLibrary;
using SharedLibrary.Auth;
using SharedLibrary.Cache;
using SharedLibrary.Middlewares;
using System.Reflection;
using System.Text;



var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.WithProperty("Application", "CentralizedLogging.Api")
    .Enrich.FromLogContext()
    .Enrich.With(new ActivityTraceEnricher())  // <-- custom enricher
    .CreateLogger();

builder.Host.UseSerilog();

var env = builder.Environment.EnvironmentName;
builder.Services.AddRedisCacheSupport(builder.Configuration, $"{env}:");
builder.Services.AddScoped<ICacheAccessProvider, CacheAccessProvider>();

// DB connection string (SQL Server example)
builder.Services.AddDbContext<LoggingDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add JwtOptions + Authentication
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;

var keyBase64 = builder.Configuration["Jwt:Key"]!;
var keyPlain = Encoding.UTF8.GetString(Convert.FromBase64String(keyBase64));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyPlain)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PolicyType.API_LEVEL, p =>
    {
        p.RequireAuthenticatedUser();
        p.Requirements.Add(new PermissionRequirement("Api"));
    });
});


builder.Services.AddHttpContextAccessor();

// Add services to the container.

builder.Services.AddControllers();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Centralized Logging API",
        Version = "v1",
        Description = "API for centralized error logging and monitoring"
    });

    // Bearer token support
    var jwtSecurityScheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Scheme = "bearer",
        BearerFormat = "JWT",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Description = "JWT auth using Bearer scheme. Paste **only** the token below.",

        Reference = new Microsoft.OpenApi.Models.OpenApiReference
        {
            Id = "Bearer",
            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme
        }
    };

    c.AddSecurityDefinition("Bearer", jwtSecurityScheme);

    // Require Bearer token for all operations (you can remove if you prefer per-endpoint)
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        { jwtSecurityScheme, Array.Empty<string>() }
    });


    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true); // ok if your Swashbuckle version supports the bool

});

// OpenTelemetry + Jaeger
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(
        serviceName: "CMLogAPI", // <- change per project
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


// Middleware should be early in the pipeline
app.UseMiddleware<RequestAudibilityMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Centralized Logging API v1");
        c.RoutePrefix = string.Empty; // Swagger UI at root "/"
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();


const string GlobalLock = "IMIS_GLOBAL_MIGRATE_SEED"; // <-- same string used in BOTH APIs
await app.MigrateAndSeedWithSqlLockAsync<LoggingDbContext>(
    connectionStringName: "DefaultConnection",           // same DB the app connects to; master catalog is derived from it
    globalLockName: GlobalLock,
    seedAsync: async (sp, ct) =>
    {
        // IMPORTANT: remove Migrate() inside your seeder
        // Old: DbSeeder.Seed(IServiceProvider) (sync). Wrap to awaitable:
        DbSeeder.Seed(sp);
        await Task.CompletedTask;
    });


app.Run();
