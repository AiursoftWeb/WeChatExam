using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers;

public class AdminController(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    ILogger<AdminController> logger) : Controller
{
    /// <summary>
    /// 管理员登录页面
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    /// <summary>
    /// 处理管理员登录请求
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(AdminLoginDto model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByNameAsync(model.Username);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(user, model.Password, isPersistent: true, lockoutOnFailure: false);

        if (result.Succeeded)
        {
            logger.LogInformation("Admin user {Username} logged in.", model.Username);
            return LocalRedirect(returnUrl ?? "/Admin/Dashboard");
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return View(model);
        }
    }

    /// <summary>
    /// 管理员退出登录
    /// </summary>
    [HttpPost]
    [AdminOnly]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        logger.LogInformation("Admin user logged out.");
        return RedirectToAction(nameof(Login));
    }

    /// <summary>
    /// 管理员控制面板
    /// </summary>
    [HttpGet]
    [AdminOnly]
    public async Task<IActionResult> Dashboard()
    {
        var users = userManager.Users.ToList();
        var totalUsers = users.Count;
        var wechatUsers = users.Count(u => !string.IsNullOrEmpty(u.MiniProgramOpenId));
        var adminUsers = 0;

        foreach (var user in users)
        {
            if (await userManager.IsInRoleAsync(user, "Admin"))
            {
                adminUsers++;
            }
        }

        ViewData["TotalUsers"] = totalUsers;
        ViewData["WeChatUsers"] = wechatUsers;
        ViewData["AdminUsers"] = adminUsers;
        ViewData["Users"] = users;

        return View();
    }
}
