using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var startup = new Startup();
        startup.ConfigureServices(builder.Configuration, builder.Environment, builder.Services);
        var app = builder.Build();
        startup.Configure(app);
        
        await app.UpdateDbAsync<TemplateDbContext>();
        await app.SeedAsync();
        await app.RunAsync();
    }
}
