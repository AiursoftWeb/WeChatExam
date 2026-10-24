using System.Net;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class PaperCollectionEditorTests : TestBase
{
    [TestCleanup]
    public override async Task CleanServer()
    {
        if (Server == null) return;
        using (var scope = Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.LegacyPaperCollectionAliases.RemoveRange(db.LegacyPaperCollectionAliases);
            db.LegacyPaperCollectionBindings.RemoveRange(db.LegacyPaperCollectionBindings);
            db.LegacyPaperCollectionCatalogs.RemoveRange(db.LegacyPaperCollectionCatalogs);
            db.PaperCollectionPapers.RemoveRange(db.PaperCollectionPapers);
            db.PaperCollections.RemoveRange(db.PaperCollections);
            await db.SaveChangesAsync();
        }
        await base.CleanServer();
    }

    [TestMethod]
    public async Task TeacherCanPreviewConnectCreateAndManageBothClientDirectories()
    {
        Assert.AreEqual(HttpStatusCode.Found, (await Http.GetAsync("/PaperCollections")).StatusCode);
        await LoginAsAdmin();
        int taxonomyId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var taxonomy = new Taxonomy { Name = "Colleges for compatibility" };
            db.Taxonomies.Add(taxonomy); await db.SaveChangesAsync(); taxonomyId = taxonomy.Id;
        }
        var previewPage = await Http.GetStringAsync($"/PaperCollections/Setup?taxonomyId={taxonomyId}");
        StringAssert.Contains(previewPage, "Connect this directory");
        using (var scope = Server!.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<PaperCollectionService>();
            var preview = await service.PreviewAsync(taxonomyId);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await PostForm("/PaperCollections/Connect", new()
                { ["taxonomyId"] = taxonomyId.ToString(), ["fingerprint"] = preview.Fingerprint }, includeToken: false)).StatusCode);
            AssertRedirect(await PostForm("/PaperCollections/Connect", new()
                { ["taxonomyId"] = taxonomyId.ToString(), ["fingerprint"] = preview.Fingerprint }, $"/PaperCollections/Setup?taxonomyId={taxonomyId}"), "/PaperCollections");
        }
        var response = await PostForm("/PaperCollections/Save", new()
        { ["Name"] = "Mixed music history", ["IsVisible"] = "true", ["OrderIndex"] = "4" }, "/PaperCollections/Create");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        var editor = await Http.GetStringAsync(response.Headers.Location);
        StringAssert.Contains(editor, "Mixed music history"); StringAssert.Contains(editor, "Add exam papers");
        Guid collectionId; int tagId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var binding = await db.LegacyPaperCollectionBindings.SingleAsync(); collectionId = binding.CollectionId; tagId = binding.TagId;
        }
        AssertRedirect(await Http.GetAsync($"/Tags/Edit/{tagId}"), $"/PaperCollections/Edit/{collectionId}");
        AssertRedirect(await Http.GetAsync($"/Tags/Delete/{tagId}"), $"/PaperCollections/Edit/{collectionId}");
        Assert.AreEqual(HttpStatusCode.OK, (await Http.GetAsync($"/PaperCollections/Preview/{collectionId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await Http.GetAsync($"/PaperCollections/Papers/{collectionId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await Http.GetAsync($"/Tags/Edit/{tagId}?fees=true")).StatusCode);
    }

}
