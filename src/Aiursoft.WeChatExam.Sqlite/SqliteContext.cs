using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;


namespace Aiursoft.WeChatExam.Sqlite;

public class SqliteContext(DbContextOptions<SqliteContext> options) : WeChatExamDbContext(options)
{
    public override Task<bool> CanConnectAsync()
    {
        return Task.FromResult(true);
    }
}
