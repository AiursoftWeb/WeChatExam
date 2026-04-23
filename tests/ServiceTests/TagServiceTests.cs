using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.InMemory;
using Aiursoft.WeChatExam.Services;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class TagServiceTests
{
    private WeChatExamDbContext? _context;
    private TagService? _tagService;

    [TestInitialize]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new InMemoryContext(options);
        _tagService = new TagService(_context);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _context?.Dispose();
    }

    [TestMethod]
    public async Task TestAddTagAsync()
    {
        var tag = await _tagService!.AddTagAsync("Test Tag");
        Assert.AreEqual("Test Tag", tag.DisplayName);
        Assert.AreEqual("TEST TAG", tag.NormalizedName);

        // Test deduplication
        var tag2 = await _tagService!.AddTagAsync("test tag ");
        Assert.AreEqual(tag.Id, tag2.Id);
        
        var count = await _context!.Tags.CountAsync();
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public async Task TestAddTagToQuestionAsync()
    {
        var questionId = Guid.NewGuid();
        var tag = await _tagService!.AddTagAsync("Tag1");
        
        await _tagService.AddTagToQuestionAsync(questionId, tag.Id);
        
        var tags = await _tagService.GetTagsForQuestionAsync(questionId);
        Assert.AreEqual(1, tags.Count);
        Assert.AreEqual(tag.Id, tags[0].Id);
        
        // Test deduplication
        await _tagService.AddTagToQuestionAsync(questionId, tag.Id);
        tags = await _tagService.GetTagsForQuestionAsync(questionId);
        Assert.AreEqual(1, tags.Count);
    }

    [TestMethod]
    public async Task TestSearchTagsAsync()
    {
        await _tagService!.AddTagAsync("Apple");
        await _tagService!.AddTagAsync("Banana");
        await _tagService!.AddTagAsync("Cherry");
        
        var results = await _tagService.SearchTagsAsync("an");
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Banana", results[0].DisplayName);
        
        results = await _tagService.SearchTagsAsync(null);
        Assert.AreEqual(3, results.Count);
    }

    [TestMethod]
    public async Task TestTagSortingAsync()
    {
        // Add tags in random order
        var tag1 = new Tag { DisplayName = "Zebra", NormalizedName = "ZEBRA", OrderIndex = 2 };
        var tag2 = new Tag { DisplayName = "Apple", NormalizedName = "APPLE", OrderIndex = 1 };
        var tag3 = new Tag { DisplayName = "Monkey", NormalizedName = "MONKEY", OrderIndex = 0 };

        await _tagService!.CreateTagAsync(tag1);
        await _tagService!.CreateTagAsync(tag2);
        await _tagService!.CreateTagAsync(tag3);

        var results = await _tagService.SearchTagsAsync(null);

        Assert.AreEqual(3, results.Count);
        Assert.AreEqual("Monkey", results[0].DisplayName); // OrderIndex 0
        Assert.AreEqual("Apple", results[1].DisplayName);  // OrderIndex 1
        Assert.AreEqual("Zebra", results[2].DisplayName);  // OrderIndex 2
    }

    [TestMethod]
    public async Task TestTagSortingByTaxonomyAsync()
    {
        int taxonomyId = 1;
        var tag1 = new Tag { DisplayName = "Zebra", NormalizedName = "ZEBRA", OrderIndex = 2, TaxonomyId = taxonomyId };
        var tag2 = new Tag { DisplayName = "Apple", NormalizedName = "APPLE", OrderIndex = 1, TaxonomyId = taxonomyId };
        
        await _tagService!.CreateTagAsync(tag1);
        await _tagService!.CreateTagAsync(tag2);

        var results = await _tagService.GetTagsByTaxonomyIdAsync(taxonomyId);

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual("Apple", results[0].DisplayName); // OrderIndex 1
        Assert.AreEqual("Zebra", results[1].DisplayName); // OrderIndex 2
    }

    [TestMethod]
    public async Task TestDeleteTagAsync()
    {
        var tag = await _tagService!.AddTagAsync("To Delete");
        var questionId = Guid.NewGuid();
        await _tagService.AddTagToQuestionAsync(questionId, tag.Id);
        
        await _tagService.DeleteTagAsync(tag.Id);
        
        var tags = await _tagService.GetAllTagsAsync();
        Assert.AreEqual(0, tags.Count);
        
        var questionTags = await _context!.QuestionTags.CountAsync();
        Assert.AreEqual(0, questionTags);
    }
}
