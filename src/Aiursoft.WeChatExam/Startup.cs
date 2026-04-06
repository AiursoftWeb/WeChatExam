using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools.Switchable;
using Aiursoft.ClickhouseLoggerProvider;
using Aiursoft.GptClient.Services;
using Aiursoft.Scanner;
using Aiursoft.WebTools.Abstractions.Models;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.InMemory;
using Aiursoft.WeChatExam.MySql;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.Authentication;
using Aiursoft.WeChatExam.Sqlite;
using Aiursoft.UiStack.Layout;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Mvc.Razor;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using SKIT.FlurlHttpClient.Wechat.Api;
using SKIT.FlurlHttpClient.Wechat.TenpayV3;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Settings;
using Aiursoft.Canon.TaskQueue;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Canon.ScheduledTasks;

namespace Aiursoft.WeChatExam;

public class Startup : IWebStartup
{
    public void ConfigureServices(IConfiguration configuration, IWebHostEnvironment environment, IServiceCollection services)
    {
        // AppSettings.
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
        services.Configure<OpenAIConfiguration>(configuration.GetSection("OpenAI"));

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

        services.AddLogging(builder =>
        {
            builder.AddClickhouse(options => configuration.GetSection("Logging:Clickhouse").Bind(options));
        });

        // Authentication and Authorization
        services.AddTemplateAuth(configuration);

        // Services
        services.AddMemoryCache();
        services.AddHttpClient();
        services.AddAssemblyDependencies(typeof(Startup).Assembly);
        services.AddScoped<IGlobalSettingsService, GlobalSettingsService>();
        services.AddScoped<IGradingService, GradingService>();
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<ITaxonomyService, TaxonomyService>();
        services.AddScoped<IPaperService, PaperService>();
        services.AddScoped<ChatClient>();
        services.AddScoped<IOllamaService, OllamaService>();
        services.AddSingleton<AiTaskService>();
        services.AddScoped<IExamService, ExamService>();
        services.AddScoped<IDistributionChannelService, DistributionChannelService>();
        services.AddScoped<IExtractService, ExtractService>();
        services.AddScoped<IOptimizationService, OptimizationService>();
        services.AddScoped<IFeedbackService, FeedbackService>();
        services.AddScoped<IVipProductService, VipProductService>();
        services.AddScoped<ICouponService, CouponService>();
        services.AddScoped<IPaperAccessService, PaperAccessService>();

        // Background job queue
        services.AddTaskQueueEngine();
        services.AddScheduledTaskEngine();
        services.RegisterBackgroundJob<Services.BackgroundJobs.DummyJob>();
        var orphanAvatarCleanupJob = services.RegisterBackgroundJob<Services.BackgroundJobs.OrphanAvatarCleanupJob>();
        services.RegisterScheduledTask(registration: orphanAvatarCleanupJob, period: TimeSpan.FromHours(6), startDelay: TimeSpan.FromMinutes(5));

        // Configure SKIT WeChat API Client
        var appSettings = configuration.GetSection("AppSettings").Get<AppSettings>()!;
        if (appSettings.WeChatEnabled)
        {
            services.AddSingleton(_ =>
            {
                var options = new WechatApiClientOptions
                {
                    AppId = appSettings.WeChat.AppId,
                    AppSecret = appSettings.WeChat.AppSecret
                };
                return new WechatApiClient(options);
            });

            services.AddScoped<IWeChatService, WeChatService>();
        }

        // Configure SKIT WeChat TenpayV3 Client for payment
        if (appSettings.WeChatEnabled && appSettings.WeChat.Payment.Enabled)
        {
            var paySettings = appSettings.WeChat.Payment;
            services.AddSingleton(_ =>
            {
                var privateKeyPem = File.ReadAllText(paySettings.PrivateKeyFilePath);
                var publicKeyPem = File.ReadAllText(paySettings.PlatformPublicKeyFilePath);
                var options = new WechatTenpayClientOptions
                {
                    MerchantId = paySettings.MchId,
                    MerchantCertificateSerialNumber = paySettings.CertificateSerialNumber,
                    MerchantCertificatePrivateKey = privateKeyPem,
                    MerchantV3Secret = paySettings.V3SecretKey,
                    AutoDecryptResponseSensitiveProperty = true,
                    PlatformAuthScheme = PlatformAuthScheme.PublicKey,
                    PlatformPublicKeyManager = new InMemoryPublicKeyManager()
                };

                options.PlatformPublicKeyManager.AddEntry(new PublicKeyEntry("RSA", paySettings.PlatformPublicKeyId, publicKeyPem));
                return new WechatTenpayClient(options);
            });

            services.AddScoped<IWeChatPayService, WeChatPayService>();
            services.AddScoped<IPaymentOrderService, PaymentOrderService>();
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

            c.AddSecurityDefinition("BearerAuth", securityScheme);

            // Only add security requirement to endpoints that are NOT decorated with [AllowAnonymous]
            c.OperationFilter<SecurityRequirementsOperationFilter>();
            c.DocumentFilter<SecurityRequirementsDocumentFilter>();

            var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            c.IncludeXmlComments(xmlPath);
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
