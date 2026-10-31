using ApiIntegrationMvc.Areas.Account.Models;
using CentralizedLogging.Contracts.DTO;
using CentralizedLogging.Sdk.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Amqp.Framing;
using Microsoft.IdentityModel.Tokens;
using SharedLibrary.Cache;
using SharedLibrary.MessageBus;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UserManagement.Contracts.DTO;
using UserManagement.Sdk.Abstractions;

namespace ApiIntegrationMvc.Areas.Account.Controllers
{
    [Area("Account")]
    public class LoginController : Controller
    {
        private readonly IUserManagementClient _users;
        private readonly ICacheAccessProvider _cache;
        private readonly IHttpContextAccessor _http;
        private readonly ICentralizedLoggingClient _centralizedlogs;
        private readonly IConfiguration _configuration;
        public LoginController(IUserManagementClient users, ICentralizedLoggingClient centralizedlogs,
            ICacheAccessProvider cache, IHttpContextAccessor http, IConfiguration configuration) => (_users, _centralizedlogs, _cache, _http, _configuration) = (users, centralizedlogs, cache, http, configuration);

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult Index()
        {
            ViewBag.Error = TempData["Error"];     // one-time error
            return View(new LoginViewModel());     // empty fields
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(LoginViewModel model, CancellationToken ct)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return View(model);
                }
                
                var req = new LoginRequest(model.Username, model.Password);
                var result = await _users.LoginAsync(req, ct);

                if (result == null || string.IsNullOrWhiteSpace(result.Token))
                {
                    TempData["Error"] = "Invalid username or password.";
                    return RedirectToAction(nameof(Index));   // ← PRG on failure
                }


                // Build claims (at least a stable user id + name)
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, result.UserId.ToString()),
                    new(ClaimTypes.Name, result.UserName),
                    new(ClaimTypes.Role, result.role),

                };

                var identity = new ClaimsIdentity(
                    claims, CookieAuthenticationDefaults.AuthenticationScheme);

                // Sign-in (creates auth cookie)
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity),
                    new AuthenticationProperties
                    {
                        IsPersistent = false,
                        // keep cookie a bit shorter than token
                        ExpiresUtc = result.ExpiresAtUtc.AddMinutes(-5)
                    });

                
                await _cache.SetAccessToken(result.Token, result.UserId, result.ExpiresAtUtc);
                await _cache.SetUserInRoleSet(result.role, result.UserId, result.ExpiresAtUtc);

                var categoriesJson = System.Text.Json.JsonSerializer.Serialize(result.Categories);
                await _cache.SetUserPermissions(categoriesJson, result.UserId, result.ExpiresAtUtc);

                return RedirectToAction("Index", "Home", new { area = "Home" });
            }
            catch (HttpRequestException hx)
            {   
                await _centralizedlogs.LogErrorAsync(BuildDto(hx, requestPath: "GET api/users"), ct);
                TempData["Error"] = hx.Message;
                return RedirectToAction(nameof(Index));   // ← PRG on failure             
            }
            catch (JsonException jx)
            {                
                TempData["Error"] = jx.Message;
                return RedirectToAction(nameof(Index));   // ← PRG on failure             
            }
            catch(Exception ex)
            {
                TempData["Error"] = "Internal Error. Please contact administrator.";
                return RedirectToAction(nameof(Index));   // ← PRG on failure             
            }
        }

        [HttpPost]        
        public async Task<IActionResult> InvalidateCacheApi(CancellationToken ct)
        {
            var secret = _configuration["ApiIntegrationMvc__ApiKey"];

            if (string.IsNullOrWhiteSpace(secret))
                return StatusCode(500);

            Request.EnableBuffering();
            if (!Request.Headers.TryGetValue("X-Request-Timestamp", out var timestamp) ||
                !Request.Headers.TryGetValue("X-Request-Signature", out var signature))
            {
                return Unauthorized();
            }

            // Reject requests older than 5 minutes.
            if (!DateTimeOffset.TryParse(
                    timestamp,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var requestTimestamp) ||
                (DateTimeOffset.UtcNow - requestTimestamp).Duration() > TimeSpan.FromMinutes(5))
            {
                return Unauthorized();
            }

            var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen:true);
            var requestBody = await reader.ReadToEndAsync(ct);
            Request.Body.Position = 0;

            var payload = $"{timestamp}:{requestBody}";

            var expectedSignature = HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret),
                Encoding.UTF8.GetBytes(payload));

            byte[] suppliedSignature;

            try
            {
                suppliedSignature = Convert.FromHexString(signature.ToString());
            }
            catch (FormatException)
            {
                return Unauthorized();
            }

            if (!CryptographicOperations.FixedTimeEquals(
                    expectedSignature, suppliedSignature))
            {
                return Unauthorized();
            }

            var role = JsonSerializer.Deserialize<RolePermissionsUpdatedEvent>(requestBody, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true}).Role;
            var deleted = await _cache.InvalidatePermissionsForRoleAsync(
                role,
                ct);
            return Ok();
        }

        private CreateErrorLogDto BuildDto(HttpRequestException ex, string requestPath)
        {
            //=>
            CreateErrorLogDto obj = new CreateErrorLogDto
           {
               ApplicationId = 4,
               Severity = "Error",
               Message = ex.Message,
               StackTrace = ex.ToString(),
               Source = ex.Source,
               RequestId = Guid.NewGuid().ToString(), // or pass one down            
           };
            return obj;
        }

        private static string? Claim(ClaimsPrincipal? u, string t) => u?.FindFirst(t)?.Value;

        private string? CurrentUserId() =>
            Claim(_http.HttpContext?.User, "sub")
         ?? Claim(_http.HttpContext?.User, ClaimTypes.NameIdentifier);


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout(CancellationToken ct)
        {
            var uid = CurrentUserId();
            if (string.IsNullOrEmpty(uid))
            {
                return RedirectToAction("Index", "Login", new { area = "Account" });
            }
            // IMPORTANT: remove tokens while the user is still authenticated
            await _cache.RemoveAsync(uid, ct);

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Login", new { area = "Account" });
        }



    }
}
