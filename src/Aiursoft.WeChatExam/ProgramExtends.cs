
namespace Aiursoft.WeChatExam;

public static class ProgramExtends
{
    public static Task<IHost> SeedAsync(this IHost host)
    {
        return Task.FromResult(host);
    }
}
