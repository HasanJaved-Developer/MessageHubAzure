using Microsoft.AspNetCore.Authorization;
using StackExchange.Redis;
using Superpower.Model;
using UserManagement.Sdk.Abstractions;

namespace ApiIntegrationMvc.Middlewares
{
    public sealed class EnsurePermissionsCachedMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IUserManagementClient _users;     // loads from API
        private readonly IDatabase _db;
        private readonly string _envPrefix;

        public EnsurePermissionsCachedMiddleware(RequestDelegate next, IUserManagementClient users, IDatabase db, IHostEnvironment env)
        {
            _next = next; _users = users; _db = db; _envPrefix = env.EnvironmentName;        // "Development", "Staging", "Production", ...
        }

        public async Task Invoke(HttpContext ctx)
        {
            var uid = ctx.User.FindFirst("sub")?.Value
                ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(uid))
            {
                // Authenticated but no id claim: let request proceed
                await _next(ctx);
                return;
            }

            if (!(ctx.User?.Identity?.IsAuthenticated ?? false))
            {
                await _next(ctx);
                return;
            }
            // Skip for static files quickly
            var path = ctx.Request.Path;
            if (path.StartsWithSegments("/css") || path.StartsWithSegments("/js") ||
                path.StartsWithSegments("/images") || path.Equals("/favicon.ico"))
            {
                await _next(ctx);
                return;
            }

            // Only for endpoints that require authorization
            var endpoint = ctx.GetEndpoint();
            var requiresAuth = endpoint?.Metadata.GetMetadata<IAuthorizeData>() is not null;

            if (!requiresAuth)
            {
                await _next(ctx);
                return;
            }

            
            var dataKey = $"{_envPrefix}:auth:permissions:{uid}";
            var cached = await _db.StringGetAsync(dataKey);

            if (!cached.HasValue)
            {
                var lockKey = $"{_envPrefix}:lock:{dataKey}";
                var token = Guid.NewGuid().ToString("N");
                // Try to acquire lock for up to ~3s
                if (await _db.StringSetAsync(lockKey, token, expiry: TimeSpan.FromSeconds(3), when: When.NotExists))
                {
                    try
                    {
                        // Double-check after winning the lock
                        cached = await _db.StringGetAsync(dataKey);
                        if (!cached.HasValue)
                        {
                            var perms = await _users.GetPermissions(int.Parse(uid), ctx.RequestAborted);
                            var payload = System.Text.Json.JsonSerializer.Serialize(perms.Categories);                            
                            await _db.StringSetAsync(dataKey, payload, expiry: TimeSpan.FromMinutes(30));
                        }
                    }
                    finally
                    {
                        // Release lock only if still owned
                        var tran = _db.CreateTransaction();
                        tran.AddCondition(Condition.StringEqual(lockKey, token));
                        _ = tran.KeyDeleteAsync(lockKey);
                        await tran.ExecuteAsync();
                    }
                }
                else
                {
                    // Another node is rebuilding — small wait then read again
                    await Task.Delay(50, ctx.RequestAborted);
                }
            }

            await _next(ctx);
        }
    }
}
