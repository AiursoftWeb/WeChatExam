using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.InMemory;
using Aiursoft.WeChatExam.Services;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class FeedbackServiceTests
{
    private WeChatExamDbContext? _context;
    private FeedbackService? _feedbackService;

    [TestInitialize]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new InMemoryContext(options);
        _feedbackService = new FeedbackService(_context);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _context?.Dispose();
    }

    [TestMethod]
    public async Task TestSubmitFeedbackAsync()
    {
        var userId = "test-user-id";
        var content = "Test content";
        var contact = "test@example.com";

        var feedback = await _feedbackService!.SubmitFeedbackAsync(userId, content, contact);

        Assert.AreEqual(userId, feedback.UserId);
        Assert.AreEqual(content, feedback.Content);
        Assert.AreEqual(contact, feedback.Contact);
        Assert.AreEqual(FeedbackStatus.Pending, feedback.Status);
        
        var count = await _context!.Feedbacks.CountAsync();
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public async Task TestGetUserFeedbacksAsync()
    {
        var userId = "user1";
        await _feedbackService!.SubmitFeedbackAsync(userId, "Content 1", null);
        await _feedbackService!.SubmitFeedbackAsync(userId, "Content 2", null);
        await _feedbackService!.SubmitFeedbackAsync("user2", "Content 3", null);

        var feedbacks = await _feedbackService.GetUserFeedbacksAsync(userId);
        Assert.AreEqual(2, feedbacks.Count);
        Assert.IsTrue(feedbacks.All(f => f.UserId == userId));
    }

    [TestMethod]
    public async Task TestSearchFeedbacksAsync()
    {
        await _feedbackService!.SubmitFeedbackAsync("u1", "C1", null);
        await Task.Delay(20);
        var f2 = await _feedbackService!.SubmitFeedbackAsync("u2", "C2", null);
        await Task.Delay(20);
        await _feedbackService.UpdateFeedbackStatusAsync(f2.Id, FeedbackStatus.Processed);

        // Search all
        var (_, total) = await _feedbackService.SearchFeedbacksAsync(1, 10);
        Assert.AreEqual(2, total);

        // Search pending
        var (pendingItems, pendingTotal) = await _feedbackService.SearchFeedbacksAsync(1, 10, FeedbackStatus.Pending);
        Assert.AreEqual(1, pendingTotal);
        Assert.AreEqual("C1", pendingItems[0].Content);

        // Search processed
        var (processedItems, processedTotal) = await _feedbackService.SearchFeedbacksAsync(1, 10, FeedbackStatus.Processed);
        Assert.AreEqual(1, processedTotal);
        Assert.AreEqual("C2", processedItems[0].Content);
    }

    [TestMethod]
    public async Task TestUpdateFeedbackStatusAsync()
    {
        var feedback = await _feedbackService!.SubmitFeedbackAsync("u1", "C1", null);
        Assert.IsNotNull(feedback);
        Assert.AreNotEqual(0, feedback.Id);
        Assert.AreEqual(FeedbackStatus.Pending, feedback.Status);

        await _feedbackService.UpdateFeedbackStatusAsync(feedback.Id, FeedbackStatus.Processed);
        
        var updated = await _feedbackService.GetFeedbackByIdAsync(feedback.Id);
        Assert.IsNotNull(updated);
        Assert.AreEqual(FeedbackStatus.Processed, updated.Status);
    }
}
