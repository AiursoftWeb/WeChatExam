using System.Net.Http.Json;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class TagSortingTests : TestBase
{
    [TestMethod]
    public async Task TestUpdateTagOrderIntegration()
    {
        // 1. Arrange
        await LoginAsAdmin();
        
        var tag1 = new Tag { DisplayName = "Tag 1", NormalizedName = "TAG 1", OrderIndex = 1 };
        var tag2 = new Tag { DisplayName = "Tag 2", NormalizedName = "TAG 2", OrderIndex = 2 };
        var tag3 = new Tag { DisplayName = "Tag 3", NormalizedName = "TAG 3", OrderIndex = 3 };

        using (var scope = Server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            context.Tags.AddRange(tag1, tag2, tag3);
            await context.SaveChangesAsync();
        }

        var ids = new[] { tag3.Id, tag1.Id, tag2.Id };
        var token = await GetAntiCsrfToken("/Tags/Index");

        // 2. Act
        var request = new HttpRequestMessage(HttpMethod.Post, "/Tags/UpdateOrder")
        {
            Content = JsonContent.Create(ids)
        };
        request.Headers.Add("RequestVerificationToken", token);
        var response = await Http.SendAsync(request);

        // 3. Assert
        response.EnsureSuccessStatusCode();

        // Refresh context to see changes
        using (var scope2 = Server!.Services.CreateScope())
        {
            var context2 = scope2.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var tags = await context2.Tags.OrderBy(t => t.OrderIndex).ToListAsync();
            
            Assert.AreEqual(3, tags.Count);
            Assert.AreEqual(tag3.Id, tags[0].Id);
            Assert.AreEqual(tag1.Id, tags[1].Id);
            Assert.AreEqual(tag2.Id, tags[2].Id);
            
            Assert.AreEqual(0, tags[0].OrderIndex);
            Assert.AreEqual(1, tags[1].OrderIndex);
            Assert.AreEqual(2, tags[2].OrderIndex);
        }
    }
}
