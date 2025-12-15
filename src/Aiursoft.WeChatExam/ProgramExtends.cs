using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam;

public static class ProgramExtends
{
    public static Task<IHost> SeedAsync(this IHost host)
    {
        return Task.FromResult(host);
    }
}
