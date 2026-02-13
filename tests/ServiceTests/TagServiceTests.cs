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
