using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools.Switchable;
using Aiursoft.Scanner;
using Aiursoft.UiStack.Layout;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Abstractions.Models;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.InMemory;
using Aiursoft.WeChatExam.MySql;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.Authentication;
using Aiursoft.WeChatExam.Sqlite;
using Aiursoft.WeChatExam.Services.BackgroundJobs;
using Aiursoft.GptClient.Services;
using Microsoft.AspNetCore.Mvc.Razor;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

/// <summary>
/// 测试专用的Startup类，用于注册Mock的WeChatService
/// </summary>
public class TestStartupWithMockWeChat : IWebStartup
{
    public static Mock<IWeChatService>? MockWeChatService { get; set; }
    public static Mock<IDistributionChannelService>? MockDistributionChannelService { get; set; }
    public static Mock<IOllamaService>? MockOllamaService { get; set; }

    public void ConfigureServices(IConfiguration configuration, IWebHostEnvironment environment, IServiceCollection services)
    {
        // AppSettings.
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));

        // Relational database
        var (connectionString, dbType, allowCache) = configuration.GetDbSettings();
        services.AddSwitchableRelationalDatabase(
            dbType: EntryExtends.IsInUnitTests() ? "InMemory" : dbType,
            connectionString: connectionString,
            supportedDbs:
            [
                new MySqlSupportedDb(allowCache: allowCache, splitQuery: false),
                new SqliteSupportedDb(allowCache: allowCache, splitQuery: true),
                new InMemorySupportedDb()
            ]);

        // Authentication and Authorization
        services.AddTemplateAuth(configuration);

        // Services
        services.AddMemoryCache();
        services.AddHttpClient();
        services.AddAssemblyDependencies(typeof(Startup).Assembly);
        services.AddScoped<IGradingService, GradingService>();
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<ITaxonomyService, TaxonomyService>();
        services.AddScoped<IPaperService, PaperService>();
        services.AddScoped<ChatClient>();
        services.AddSingleton<AiTaskService>();
        services.AddScoped<IExamService, ExamService>();
        services.AddScoped<IExtractService, ExtractService>();
        services.AddScoped<IOptimizationService, OptimizationService>();

        // Background job queue
        services.AddSingleton<BackgroundJobQueue>();
        services.AddHostedService<QueueWorkerService>();

        // Configure Mock SKIT WeChat API Client instead of real one
        if (MockWeChatService != null
        && MockDistributionChannelService != null)
        {
            services.AddScoped(_ => MockWeChatService.Object);
            services.AddScoped(_ => MockDistributionChannelService.Object);
        }

        if (MockOllamaService != null)
        {
            services.AddScoped(_ => MockOllamaService.Object);
        }
        else
        {
            services.AddScoped<IOllamaService, OllamaService>();
        }

        // Add Razor Pages and MVC for admin web interface
        services.AddControllersWithViews();
        services.AddRazorPages();
        services.AddAssemblyDependencies(typeof(Startup).Assembly);
        services.AddSingleton<NavigationState<Startup>>();

        // Controllers
        services.AddControllers()
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
                options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            })
            .AddApplicationPart(typeof(Startup).Assembly)
            .AddApplicationPart(typeof(UiStackLayoutViewModel).Assembly)
            .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
            .AddDataAnnotationsLocalization();

        services.AddSwaggerGen(c =>
        {
            // Only include API controllers (exclude Management controllers which return views)
            c.DocInclusionPredicate((_, apiDesc) => apiDesc.RelativePath?.StartsWith("api/") == true);

            // Add JWT Bearer support in Swagger
            var securityScheme = new Microsoft.OpenApi.OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "Enter 'Bearer {token}'",
                In = Microsoft.OpenApi.ParameterLocation.Header,
                Type = Microsoft.OpenApi.SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            };

            c.AddSecurityDefinition("Bearer", securityScheme);

            // Only add security requirement to endpoints that are NOT decorated with [AllowAnonymous]
            c.OperationFilter<SecurityRequirementsOperationFilter>();
        });
    }

    public void Configure(WebApplication app)
    {
        app.UseExceptionHandler("/Error/Error");
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.MapDefaultControllerRoute();
    }
}
