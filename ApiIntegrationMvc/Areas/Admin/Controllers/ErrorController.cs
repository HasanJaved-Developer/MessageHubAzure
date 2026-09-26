using CentralizedLogging.Sdk.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary;
using SharedLibrary.Auth;
using SharedLibrary.Cache;

namespace ApiIntegrationMvc.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ErrorController : Controller
    {
        private readonly ICentralizedLoggingClient _centralizedlogs;
        private readonly ICacheAccessProvider _cache;
        private readonly IHttpContextAccessor _http;
        public ErrorController(ICentralizedLoggingClient centralizedlogs, ICacheAccessProvider cache, IHttpContextAccessor http) => (_centralizedlogs, _cache, _http) = (centralizedlogs, cache, http);

        [Authorize(Policy = PolicyType.WEB_LEVEL)]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            string token = await _cache.GetAccessTokenAsync(ct);

            try
            {
                var result = await _centralizedlogs.GetAllErrorAsync(ct);
                return View(result.OrderByDescending(v => v.Id));
            }
            catch (PermissionDeniedException ex) when (ex.StatusCode == 403)
            {
                TempData["Error"] = "You do not have permission to view system error logs.";
                return RedirectToAction("Index", "Home", new { area = "Home" });
            }
        }
    }
}
