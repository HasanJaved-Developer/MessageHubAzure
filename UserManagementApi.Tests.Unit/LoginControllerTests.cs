using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using SharedLibrary.Cache;
using SharedLibrary.MessageBus;
using UserManagement.Contracts.DTO;
using UserManagementApi.Data;
using UserManagementApi.DTO;
using UserManagementApi.DTO.Auth;

namespace UserManagementApi.Tests.Unit
{
    public class LoginControllerTests
    {
        private readonly ICacheAccessProvider _cache;
        private readonly IPermissionsEventPublisher _publisher;
        private static AppDbContext NewDb(string name)
        {
            var opts = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(name)
                .Options;
            return new AppDbContext(opts);
        }

        [Fact]
        public async Task Authenticate_WhenMissingUsernameOrPassword_Returns400WithValidationProblem()
        {
            using var db = NewDb(nameof(Authenticate_WhenMissingUsernameOrPassword_Returns400WithValidationProblem));
            var jwtOptions = new JwtOptions
            {
                Issuer = "test-issuer",
                Audience = "test-audience",
                Key = "supersecretkey1234567890"
            };
            var wrapped = Options.Create(jwtOptions); // returns IOptions<JwtOptions>

            var controller = new TestableLoginController(_cache, db, wrapped, _publisher);

            var res = await controller.Authenticate(new LoginRequest("", ""));

            var bad = res.Result.Should().BeOfType<BadRequestObjectResult>().Which;
            bad.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            bad.Value.Should().BeOfType<ValidationProblemDetails>();
            var v = (ValidationProblemDetails)bad.Value!;
            v.Errors.Should().ContainKey("userName");
            v.Errors.Should().ContainKey("password");
        }
    }
}
