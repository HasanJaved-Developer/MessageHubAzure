using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Distributed;
using SharedLibrary.Cache;
using System.Text.Json;
using UserManagementApi.Contracts.Models;
using static System.Net.WebRequestMethods;

namespace SharedLibrary.Auth
{
    public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IHttpContextAccessor _http;
        private readonly IDistributedCache _cache;
        private readonly ICacheAccessProvider _tokens;

        public PermissionHandler(IHttpContextAccessor http, IDistributedCache cache, ICacheAccessProvider tokens) => (_http, _cache, _tokens) = (http, cache, tokens);

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            var userId =
                context.User.FindFirst("sub")?.Value ??
                context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
                return; // no user → no success

            var rd = _http.HttpContext!.GetRouteData();
            var area = rd.Values["area"]?.ToString() ?? "";
            var controller = rd.Values["controller"]?.ToString() ?? "";
            var action = rd.Values["action"]?.ToString() ?? "";

            var ct = _http.HttpContext?.RequestAborted ?? default;
            var permissions = await _tokens.GetUserPermissionsAsync(ct);

            IReadOnlyList<Category> categories = new List<Category>();
            if (permissions != null)
            {
                categories = JsonSerializer.Deserialize<List<Category>>(permissions);
            }

            if (categories is null) return;

            bool IsAllowed = false;
            if (requirement.Permission == "Web")
                IsAllowed = categories.Any( c => c.Modules.Any( m => m.Area == area && m.Controller == controller && m.Action == action && m.Type == "WebApp"));
            else if (requirement.Permission == "Api")
                IsAllowed = categories.Any(c => c.Modules.Any(m => m.Controller == controller && m.Action == action && m.Type == "Api"));


            if (IsAllowed)
                context.Succeed(requirement);
            else
            {
                context.Fail();
            }
        }
    }
}
