using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UserManagement.Contracts.DTO
{
    public record LoginRequest(string UserName, string Password);
    public record UpdatePermissionsRequest(string Op);
    public record UpdatePermissionsResponse(string Message);
    public record FunctionDto(int Id, string Code, string DisplayName);
    public record ModuleDto(int Id, string Name, string Area, string Controller, string Action, string Type, List<FunctionDto> Functions);
    public record CategoryDto(int Id, string Name, List<ModuleDto> Modules);
    public record UserPermissionsDto(int UserId, string UserName, List<CategoryDto> Categories);
}
