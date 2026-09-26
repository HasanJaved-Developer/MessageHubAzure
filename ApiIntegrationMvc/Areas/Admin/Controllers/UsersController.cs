using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Auth;

namespace ApiIntegrationMvc.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class UsersController : Controller
    {
        [Authorize(Policy = PolicyType.WEB_LEVEL)]
        public IActionResult Index()
        {
            return RedirectToAction("Index", "Home", new { area = "Home" });
            //return View();
        }
    }
}
