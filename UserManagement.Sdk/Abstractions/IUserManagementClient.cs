using Microsoft.AspNetCore.Mvc;
using UserManagement.Contracts.Auth;
using UserManagement.Contracts.DTO;

namespace UserManagement.Sdk.Abstractions
{
    public interface IUserManagementClient
    {
        Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
        Task<UpdatePermissionsResponse> UpdatePermissions(UpdatePermissionsRequest operations, CancellationToken ct = default);
        Task<UserPermissionsDto> GetPermissions(int userId, CancellationToken ct = default);
        Task<object> GetState(int userId, CancellationToken ct = default);


    }
}
