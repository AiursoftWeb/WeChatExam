using Aiursoft.DbTools;
using Aiursoft.DbTools.Sqlite;
using Aiursoft.WeChatExam.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.WeChatExam.Sqlite;

public class SqliteSupportedDb(bool allowCache, bool splitQuery) : SupportedDatabaseType<WeChatExamDbContext>
{
    public override string DbType => "Sqlite";

    public override IServiceCollection RegisterFunction(IServiceCollection services, string connectionString)
    {
        return services.AddAiurSqliteWithCache<SqliteContext>(
            connectionString,
            splitQuery: splitQuery,
            allowCache: allowCache);
    }

    public override WeChatExamDbContext ContextResolver(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<SqliteContext>();
    }
}
