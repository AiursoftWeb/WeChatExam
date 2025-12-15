using Aiursoft.DbTools;
using Aiursoft.DbTools.MySql;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.WeChatExam.MySql;

public class MySqlSupportedDb(bool allowCache, bool splitQuery) : SupportedDatabaseType<TemplateDbContext>
{
    public override string DbType => "MySql";

    public override IServiceCollection RegisterFunction(IServiceCollection services, string connectionString)
    {
        return services.AddAiurMySqlWithCache<MySqlContext>(
                connectionString, 
            splitQuery: splitQuery,
            allowCache: allowCache);
    }

    public override TemplateDbContext ContextResolver(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<MySqlContext>();
    }
}
