using Aiursoft.WeChatExam.Entities;
using Aiursoft.Scanner.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// 数据库初始化服务，负责创建默认管理员账户
/// </summary>
public class DatabaseInitializer(
    UserManager<User> userManager,
    RoleManager<IdentityRole> roleManager,
    ILogger<DatabaseInitializer> logger) : IScopedDependency
{
    public async Task InitializeAsync()
    {
        // 创建 Admin 角色
        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new IdentityRole("Admin"));
            logger.LogInformation("Created Admin role");
        }

        // 检查是否已存在管理员用户
        var adminUser = await userManager.FindByNameAsync("admin");
        if (adminUser == null)
        {
            // 创建默认管理员账户
            adminUser = new User
            {
                UserName = "admin",
                DisplayName = "系统管理员",
                Email = "admin@example.com",
                EmailConfirmed = true,
                AvatarRelativePath = User.DefaultAvatarPath
            };

            var result = await userManager.CreateAsync(adminUser, "admin123");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
                logger.LogInformation("Created default admin user: username=admin, password=Admin123!");
            }
            else
            {
                logger.LogError("Failed to create admin user: {Errors}",
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
    }
}
