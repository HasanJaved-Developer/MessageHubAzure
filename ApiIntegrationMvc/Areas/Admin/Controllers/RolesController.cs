using CentralizedLogging.Sdk.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary;
using SharedLibrary.Auth;
using SharedLibrary.Cache;
using StackExchange.Redis;
using System.Security.Claims;
using UserManagement.Contracts.DTO;
using UserManagement.Sdk.Abstractions;

namespace ApiIntegrationMvc.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class RolesController : Controller
    {
        private readonly IUserManagementClient _IUserManagementClient;        
        private readonly ICacheAccessProvider _cache;        
        public RolesController(IUserManagementClient userManagementClient, ICacheAccessProvider cache) => (_IUserManagementClient, _cache) = (userManagementClient, cache);

        [Authorize(Policy = PolicyType.WEB_LEVEL)]
        public IActionResult Index()
        {
            return RedirectToAction("Index", "Home", new { area = "Home" });         
        }

        [HttpPost]
        public async Task<IActionResult> UpdatePermissions([FromBody] UpdatePermissionsRequest operations, CancellationToken ct)
        {
            string token = await _cache.GetAccessTokenAsync(ct);

            try
            {
                UpdatePermissionsResponse resp = await _IUserManagementClient.UpdatePermissions(operations, ct);
                return Ok(resp);
            }
            catch (PermissionDeniedException ex) when (ex.StatusCode == 403)
            {
                TempData["Error"] = "You do not have permission to view system error logs.";
                return RedirectToAction("Index", "Home", new { area = "Home" });
            }
            catch (HttpRequestException hx)
            {
                TempData["Error"] = "Internal exception has occurred.";
                return RedirectToAction("Index", "Home", new { area = "Home" });
            }
        }

        [HttpGet] 
        public async Task<IActionResult> GetState(CancellationToken ct)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            object result = await _IUserManagementClient.GetState(int.Parse(userId)); 
            return Ok(result);
        }
    }
}
