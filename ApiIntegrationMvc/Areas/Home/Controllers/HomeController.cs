using ApiIntegrationMvc.Areas.Account.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Cache;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using UserManagement.Contracts.Auth;
using UserManagement.Sdk.Abstractions;
using UserManagementApi.Contracts.Models;
using static System.Net.WebRequestMethods;

namespace ApiIntegrationMvc.Areas.Home.Controllers
{
    [Area("Home")]
    public class HomeController : Controller
    {
        private readonly ICacheAccessProvider _tokens;


        public HomeController(ICacheAccessProvider tokens) => _tokens = tokens;
                
        public IActionResult Index(CancellationToken ct)
        {            
            return View();
        }


        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult Denied()
        {
            TempData["Toast"] = "Your permissions have been updated.";
            return RedirectToAction("Index", "Home", new { area = "Home" });
        }



    }
}
