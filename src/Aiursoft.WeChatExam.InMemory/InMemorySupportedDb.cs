using Aiursoft.DbTools;
using Aiursoft.DbTools.InMemory;
using Aiursoft.WeChatExam.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.WeChatExam.InMemory;

public class InMemorySupportedDb : SupportedDatabaseType<WeChatExamDbContext>
{
    public override string DbType => "InMemory";

    public override IServiceCollection RegisterFunction(IServiceCollection services, string connectionString)
    {
        return services.AddAiurInMemoryDb<InMemoryContext>();
    }

    public override WeChatExamDbContext ContextResolver(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<InMemoryContext>();
    }
}
