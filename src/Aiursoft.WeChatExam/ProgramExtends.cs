
using Aiursoft.CSTools.Tools;
using Aiursoft.WeChatExam.Services;

namespace Aiursoft.WeChatExam;

public static class ProgramExtends
{
    public static async Task<IHost> SeedAsync(this IHost host)
    {
        // Skip database initialization in unit tests
        if (EntryExtends.IsInUnitTests())
        {
            return host;
        }

        using var scope = host.Services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        await initializer.InitializeAsync();
        return host;
    }
}
