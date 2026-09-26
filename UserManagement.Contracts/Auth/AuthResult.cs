using UserManagement.Contracts.DTO;

namespace UserManagement.Contracts.Auth
{    
    public record AuthResponse(
      int UserId,
      string UserName,
      string Token,
      string role,
      DateTime ExpiresAtUtc,
      List<CategoryDto> Categories
  );
}
