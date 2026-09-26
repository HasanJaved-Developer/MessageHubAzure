using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using SharedLibrary;
using SharedLibrary.Cache;
using SharedLibrary.MessageBus;
using SharedLibrary.Middlewares;
using System.Reflection;
using System.Text;
using UserManagementApi;
using UserManagementApi.Data;
using UserManagementApi.DTO.Auth;
using UserManagementApi.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.WithProperty("Application", "UserManagement.Api")
    .Enrich.FromLogContext()
    .Enrich.With(new ActivityTraceEnricher())  // <-- custom enricher
    .CreateLogger();

builder.Host.UseSerilog();

var env = builder.Environment.EnvironmentName;
builder.Services.AddRedisCacheSupport(builder.Configuration, $"{env}:");

builder.Services.AddScoped<ICacheAccessProvider, CacheAccessProvider>();

// DB connection string (SQL Server example)
builder.Services.AddDbContext<AppDbContext>(options =>
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

builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();

builder.Services.AddControllersWithViews();


// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "User Management API",
        Version = "v1",
        Description = "API for user authentication, authorization and management"
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
        serviceName: "UserAPI", // <- change per project
        serviceVersion: "1.0.0"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation(o => { o.RecordException = true; })
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri($"http://{builder.Configuration["JAEGER_HOST"]}:4317"); // host -> docker            
            o.Protocol = OtlpExportProtocol.Grpc;
        }));

// RabbitMQ Bind options
builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMq"));

// Register RabbitMQ connection + publisher
builder.Services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();
builder.Services.AddSingleton<IPermissionsEventPublisher, PermissionsEventPublisher>();

var app = builder.Build();
app.UseSerilogRequestLogging(); // structured request logs

app.MapGet("/health", () => Results.Ok("OK"));

// Middleware should be early in the pipeline
app.UseMiddleware<RequestAudibilityMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{    
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "User Management API v1");
        c.RoutePrefix = string.Empty; // Swagger UI at root "/"
    });
}

app.UseHttpsRedirection();


app.UseAuthentication(); // must be before UseAuthorization
app.UseAuthorization();

app.MapControllers();

// Seed sample data
//DbSeeder.Seed(app.Services);

const string GlobalLock = "IMIS_GLOBAL_MIGRATE_SEED"; // <-- same string used in BOTH APIs
await app.MigrateAndSeedWithSqlLockAsync<AppDbContext>(
    connectionStringName: "DefaultConnection",           // same DB the app connects to; master catalog is derived from it
    globalLockName: GlobalLock,
    seedAsync: async (sp, ct) =>
    {
        // IMPORTANT: remove Migrate() inside your seeder
        // Old: DbSeeder.Seed(IServiceProvider) (sync). Wrap to awaitable:
        DbSeeder.SeedCore(sp);
        DbSeeder.SeedFeatureApiPermissions(sp);
        await Task.CompletedTask;
    });


app.Run();
public partial class Program { }  // make Program discoverable for tests