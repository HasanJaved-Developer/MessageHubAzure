using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SharedLibrary.Cache;
using SharedLibrary.MessageBus;
using StackExchange.Redis;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using UserManagement.Contracts.Auth;
using UserManagement.Contracts.DTO;
using UserManagementApi.Contracts.Models;
using UserManagementApi.Data;
using UserManagementApi.DTO.Auth;
using static System.Net.WebRequestMethods;


namespace UserManagementApi.Controllers
{

    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly ICacheAccessProvider _cache;
        private readonly AppDbContext _db;
        private readonly JwtOptions _jwt;
        private readonly IPermissionsEventPublisher _publisher;

        public UsersController(ICacheAccessProvider cache, AppDbContext db, IOptions<JwtOptions> jwtOptions, IPermissionsEventPublisher publisher) => (_cache, _db, _jwt, _publisher) = (cache, db, jwtOptions.Value, publisher);
        
        // --------- NEW: POST /api/users/authenticate ----------
        [HttpPost("authenticate")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]

        public async Task<ActionResult<AuthResponse>> Authenticate([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.UserName) || string.IsNullOrWhiteSpace(req.Password))
            {
                return ValidationProblem(new ValidationProblemDetails
                {
                    Title = "Validation error",
                    Detail = "Username and password are required.",
                    Status = StatusCodes.Status400BadRequest,
                    Errors = new Dictionary<string, string[]>
                    {
                        ["userName"] = new[] { "Required" },
                        ["password"] = new[] { "Required" }
                    }
                });
            }

            var user = await _db.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserName == req.UserName);

            if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.Password))
            {                
                return Unauthorized(new ProblemDetails
                {
                    Title = "Invalid credentials",
                    Detail = "Username or password is incorrect.",
                    Status = StatusCodes.Status401Unauthorized
                });
            }
        
   
            var dto = await BuildPermissionsForUser(user.Id);
                        
            var token = GenerateJwt(user, out var expiresAtUtc);

            var role = user?.UserRoles.Select(ur => ur.Role.Name).FirstOrDefault();

            return Ok(new AuthResponse(user.Id, user.UserName, token, role, expiresAtUtc, dto.Categories));
        }

        [HttpPost("updatepermissions")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<AuthResponse>> UpdatePermissions([FromBody] UpdatePermissionsRequest operation)
        {
            var admin = await _db.Roles.Where(r => r.Name == "Admin").FirstOrDefaultAsync();
            var webfunction = await _db.Functions.Where(r => r.Code == "Logs.View").FirstOrDefaultAsync();
            var apifunction = await _db.Functions.Where(r => r.Code == "Api.Logs.View").FirstOrDefaultAsync();

            bool hasWebAccess = _db.RoleFunctions.Any(rf => rf.FunctionId == webfunction.Id && rf.RoleId == admin.Id);
            bool hasApiAccess = _db.RoleFunctions.Any(rf => rf.FunctionId == apifunction.Id && rf.RoleId == admin.Id);

            if (operation.Op == "grant-webapp" && !hasWebAccess)
            {                
                await _db.RoleFunctions.AddAsync(new RoleFunction() { RoleId = admin.Id, FunctionId = webfunction.Id});                
            }
            if (operation.Op == "grant-api" && !hasApiAccess)
            {
                await _db.RoleFunctions.AddAsync(new RoleFunction() { RoleId = admin.Id, FunctionId = apifunction.Id });
            }
            if (operation.Op == "revoke-api" && hasApiAccess)
            {
                _db.RoleFunctions.Remove(new RoleFunction() { RoleId = admin.Id, FunctionId = apifunction.Id });
            }
            if (operation.Op == "revoke-both")
            {
                if (hasWebAccess)
                    _db.RoleFunctions.Remove(new RoleFunction() { RoleId = admin.Id, FunctionId = webfunction.Id });
                if (hasApiAccess)
                    _db.RoleFunctions.Remove(new RoleFunction() { RoleId = admin.Id, FunctionId = apifunction.Id });
            }
            // save once (only if something changed)
            if (_db.ChangeTracker.HasChanges())
            {
                var affected = await _db.SaveChangesAsync();                
            }


            // Fire-and-forget style, but we still return Task
            var evt = new RolePermissionsUpdatedEvent
            {
                Role = "Admin",
                OccurredAtUtc = DateTime.UtcNow,
                Source = "UserManagementApi"
            };
            
            // 2) Publish event to RabbitMQ (after commit)
            await _publisher.PublishRolePermissionsUpdatedAsync(evt);
                        
            return Ok(new { Message = $"Permissions updated with operation: {operation.Op}" });
        }

        // --------- (Existing) GET /api/users/{userId}/permissions ----------
        // Now protected by JWT; call with Bearer token returned by /authenticate
        [Authorize]
        [HttpGet("{userId:int}/permissions")]
        public async Task<ActionResult> GetPermissions(int userId)
        {
            // Optional: you can enforce that a user can only view their own permissions
            // by comparing userId with the token's sub, if desired.

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return NotFound($"User {userId} not found.");

            var dto = await BuildPermissionsForUser(userId);
            return Ok(dto);
        }

        [Authorize]
        [HttpGet("{userId:int}/getstate")]
        public async Task<object> GetStateAsync(int userId)
        {
            var admin = await _db.Roles.Where(r => r.Name == "Admin").FirstOrDefaultAsync();
            var webfunction = await _db.Functions.Where(r => r.Code == "Logs.View").FirstOrDefaultAsync();
            var apifunction = await _db.Functions.Where(r => r.Code == "Api.Logs.View").FirstOrDefaultAsync();

            bool hasWebAccess = _db.RoleFunctions.Any(rf => rf.FunctionId == webfunction.Id && rf.RoleId == admin.Id);
            bool hasApiAccess = _db.RoleFunctions.Any(rf => rf.FunctionId == apifunction.Id && rf.RoleId == admin.Id);

            return new
            {
                webapp = hasWebAccess,
                api = hasApiAccess
            };
        }

        // ----- helpers -----

        protected virtual async Task<UserPermissionsDto> BuildPermissionsForUser(int userId)
        {
            // Fetch the user (for name in DTO)
            var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

            // 1) Get all (Category, Module, Function) triples the user is allowed to access.
            //    This is fully translatable SQL: joins + where.
            var triples = await (
                from ur in _db.UserRoles.AsNoTracking()
                where ur.UserId == userId
                join rf in _db.RoleFunctions.AsNoTracking() on ur.RoleId equals rf.RoleId
                join f in _db.Functions
                            .Include(x => x.Module)
                                .ThenInclude(m => m.Category)
                            .AsNoTracking()
                      on rf.FunctionId equals f.Id
                select new
                {
                    CategoryId = f.Module.Category.Id,
                    CategoryName = f.Module.Category.Name,
                    ModuleId = f.Module.Id,
                    ModuleName = f.Module.Name,    
                    ModuleType = f.Module.Type,
                    f.Module.Area,
                    f.Module.Controller,
                    f.Module.Action,
                    FunctionId = f.Id,
                    f.Code,
                    f.DisplayName
                }
            ).ToListAsync();

            // 2) Group in memory to shape the hierarchical DTOs.
            var categoryDtos = triples
                .GroupBy(t => new { t.CategoryId, t.CategoryName })
                .Select(cg => new CategoryDto(
                    cg.Key.CategoryId,
                    cg.Key.CategoryName,
                    cg.GroupBy(t => new { t.ModuleId, t.ModuleName, t.Area, t.Controller, t.Action, t.ModuleType})
                      .Select(mg => new ModuleDto(
                          mg.Key.ModuleId,
                          mg.Key.ModuleName,
                          mg.Key.Area,
                          mg.Key.Controller,
                          mg.Key.Action,
                          mg.Key.ModuleType,
                          mg.GroupBy(x => new { x.FunctionId, x.Code, x.DisplayName }) // distinct functions
                            .Select(g => new FunctionDto(g.Key.FunctionId, g.Key.Code, g.Key.DisplayName))
                            .OrderBy(f => f.Code)
                            .ToList()
                      ))
                      .OrderBy(m => m.Id)
                      .ToList()
                ))
                .OrderBy(c => c.Name)
                .ToList();

            return new UserPermissionsDto(user.Id, user.UserName, categoryDtos);
        }
                
        protected virtual string GenerateJwt(AppUser user, out DateTime expiresAtUtc)
        {
            var keyBase64 = _jwt.Key;
            var keyPlain = Encoding.UTF8.GetString(Convert.FromBase64String(keyBase64));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyPlain));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
                        
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.UniqueName, user.UserName)                
            };
            
            var now = DateTime.UtcNow;
            expiresAtUtc = now.AddMinutes(_jwt.ExpiresMinutes);

            var token = new JwtSecurityToken(
                issuer: _jwt.Issuer,
                audience: _jwt.Audience,
                claims: claims,
                notBefore: now,
                expires: expiresAtUtc,
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        
    }
}
