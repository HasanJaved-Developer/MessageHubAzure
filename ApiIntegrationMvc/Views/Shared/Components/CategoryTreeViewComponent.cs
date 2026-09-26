using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Cache;
using System.Text.Json;
using UserManagementApi.Contracts.Models;

namespace ApiIntegrationMvc.Views.Shared.Components
{

    public sealed class CategoryTreeViewComponent: ViewComponent
    {
        private readonly ICacheAccessProvider _tokens;
        public CategoryTreeViewComponent(ICacheAccessProvider tokens)
        => _tokens = tokens;
    
        public async Task<IViewComponentResult> InvokeAsync()
        {
            var ct = HttpContext?.RequestAborted ?? default;            

            var permissions = await _tokens.GetUserPermissionsAsync(ct);            
            
            IReadOnlyList<Category> categories = new List<Category>();
            if (permissions != null)
            {
                categories = JsonSerializer.Deserialize<List<Category>>(permissions);
            }

            return View(categories); // Views/Shared/Components/CategoryTree/Default.cshtml
        }
    }
}
