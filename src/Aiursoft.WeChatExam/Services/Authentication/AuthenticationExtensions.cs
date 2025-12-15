
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Aiursoft.WeChatExam.Services.Authentication;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddTemplateAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var appSettings = configuration.GetSection("AppSettings").Get<AppSettings>()!;
        
        services.AddIdentity<User, IdentityRole>(options =>
            {
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireDigit = false;
                options.Password.RequiredLength = 6;
                options.Password.RequiredUniqueChars = 0;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
            })
            .AddEntityFrameworkStores<TemplateDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<IUserClaimsPrincipalFactory<User>, UserClaimsPrincipalFactory>();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "Bearer";
            options.DefaultChallengeScheme = "Bearer";
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                // In production, you should use a secure key from configuration
                IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(appSettings.WechatAppSecret))
            };
        });

        services.AddAuthorization();
        return services;
    }
}
