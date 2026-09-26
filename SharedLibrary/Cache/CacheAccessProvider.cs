using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Hosting;

using StackExchange.Redis;
using System.Data;
using System.Diagnostics;
using System.Security;
using System.Security.Claims;
using System.Text.Json;
using UserManagementApi.Contracts.Models;

namespace SharedLibrary.Cache
{
    public sealed class CacheAccessProvider : ICacheAccessProvider
    {
        private readonly IDatabase _db;
        private readonly IDistributedCache _cache;
        private readonly IHttpContextAccessor _http;        
        private readonly string _envPrefix;
        public CacheAccessProvider(IDatabase db, IDistributedCache cache, IHttpContextAccessor http, IHostEnvironment env) => (_db, _cache, _http, _envPrefix) = (db, cache, http, 
            string.IsNullOrWhiteSpace(env.EnvironmentName) ? "" : env.EnvironmentName + ":");

        private string K(string key) => _envPrefix + key;

        public async Task<long> InvalidatePermissionsForRoleAsync(string roleName, CancellationToken ct = default)
        {
            
            var members = await _db.SetMembersAsync(K($"auth:roleusers:{roleName}"));
            if (members.Length == 0)
                return 0;
            
            var keys = members
                .Select(m => (RedisKey)K($"auth:permissions:{(int)m}"))
                .ToArray();

            return await _db.KeyDeleteAsync(keys);
        }

        public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
        {
            var user = _http.HttpContext?.User;
            var uid =
                user?.FindFirst("sub")?.Value
             ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(uid)) return null;

            var key = $"auth:token:{uid}";
            var token = await _cache.GetStringAsync(key, ct);
            return token;            
        }

        public async Task<string?> GetUserPermissionsAsync(CancellationToken ct = default)
        {
            var user = _http.HttpContext?.User;
            var uid =
                user?.FindFirst("sub")?.Value
             ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(uid)) return null;

            var dataKey = K($"auth:permissions:{uid}");
            return await _db.StringGetAsync(dataKey);
            
        }

        // Optional helper method to set the token into cache
        public async Task SetAccessToken(string token, int userId, DateTime expiresAtUtc, CancellationToken ct = default)
        {
            var ttl = ToTtl(expiresAtUtc);

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            };

            // IMPORTANT: Do not manually prefix here if using InstanceName in startup.
            var key = $"auth:token:{userId}";

            await _cache.SetStringAsync(key, token, options, ct);
        }

        public async Task<bool> SetUserInRoleSet(string roleName, int userId, DateTime expiresAtUtc, CancellationToken ct = default)
        {
            var ttl = ToTtl(expiresAtUtc);

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            };

            // IMPORTANT: Do not manually prefix here if using InstanceName in startup.
            var usersForRoleKey = K($"auth:roleusers:{roleName}");

            bool added = await _db.SetAddAsync(usersForRoleKey, userId);
            return added;
        }

        public async Task SetUserPermissions(string permissions, int userId, DateTime expiresAtUtc, CancellationToken ct = default)
        {
            var ttl = ToTtl(expiresAtUtc);
            
            // IMPORTANT: Do not manually prefix here if using InstanceName in startup.
            var key = K($"auth:permissions:{userId}");            
            await _db.StringSetAsync(key, permissions, ttl);
            
        }
        
        //User Logout
        public Task RemoveAsync(string userId, CancellationToken ct = default)
        {   
            _cache.Remove($"auth:token:{userId}");            
            return Task.CompletedTask;
        }

        public static TimeSpan ToTtl(DateTime expiresAtUtc, TimeSpan? safety = null)
        {
            // ensure it's treated as UTC
            if (expiresAtUtc.Kind != DateTimeKind.Utc)
                expiresAtUtc = DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc);

            var ttl = expiresAtUtc - DateTime.UtcNow;

            // subtract a small safety margin to avoid edge expiries
            ttl -= safety ?? TimeSpan.FromSeconds(15);

            return ttl > TimeSpan.Zero ? ttl : TimeSpan.Zero;
        }
    }
}
