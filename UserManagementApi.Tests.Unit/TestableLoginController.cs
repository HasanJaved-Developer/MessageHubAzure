using Microsoft.Extensions.Options;
using SharedLibrary.Cache;
using SharedLibrary.MessageBus;
using UserManagement.Contracts.DTO;
using UserManagementApi.Contracts.Models;
using UserManagementApi.Controllers;
using UserManagementApi.Data;
using UserManagementApi.DTO.Auth;

namespace UserManagementApi.Tests.Unit
{
    public sealed class TestableLoginController :UsersController
    {
        private readonly ICacheAccessProvider _cache;
        private readonly string _jwtToReturn;
        private readonly DateTime _expToReturn;
        private readonly UserPermissionsDto _permToReturn;

        public TestableLoginController(ICacheAccessProvider cache, AppDbContext db,
                                       IOptions<JwtOptions> jwtopts, IPermissionsEventPublisher publishers)
            : base(cache, db, jwtopts, publishers)
        {
            _jwtToReturn = "fake-jwt";
            _expToReturn = DateTime.UtcNow.AddHours(1);
            _permToReturn = new UserPermissionsDto(0, "abc", new List<CategoryDto>());

        }
        protected override Task<UserPermissionsDto> BuildPermissionsForUser(int userId)
            => Task.FromResult(_permToReturn);

        protected override string GenerateJwt(AppUser user, out DateTime expiresAtUtc)
        {
            expiresAtUtc = _expToReturn;
            return _jwtToReturn;
        }
    }
}
