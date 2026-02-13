using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Moq;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class GradingServiceTests
{
    private Mock<IOllamaService>? _mockOllamaService;
    private GradingService? _gradingService;

    [TestInitialize]
    public void Setup()
    {
        _mockOllamaService = new Mock<IOllamaService>();
        _gradingService = new GradingService(_mockOllamaService.Object);
    }

    [TestMethod]
    public async Task TestGradeExactMatch_Correct()
    {
        var result = await _gradingService!.GradeAsync("Answer", "Answer", GradingStrategy.ExactMatch, 10, "Question");
        Assert.IsTrue(result.IsCorrect);
        Assert.AreEqual(10, result.Score);
    }

    [TestMethod]
    public async Task TestGradeExactMatch_Incorrect()
    {
        var result = await _gradingService!.GradeAsync("Wrong", "Answer", GradingStrategy.ExactMatch, 10, "Question");
        Assert.IsFalse(result.IsCorrect);
        Assert.AreEqual(0, result.Score);
    }

    [TestMethod]
    public async Task TestGradeExactMatch_CaseInsensitive()
    {
        var result = await _gradingService!.GradeAsync("answer", "Answer", GradingStrategy.ExactMatch, 10, "Question");
        Assert.IsTrue(result.IsCorrect);
        Assert.AreEqual(10, result.Score);
    }

    [TestMethod]
    public async Task TestGradeFuzzyMatch_Correct()
    {
        var result = await _gradingService!.GradeAsync("This is the Answer", "Answer", GradingStrategy.FuzzyMatch, 10, "Question");
        Assert.IsTrue(result.IsCorrect);
        Assert.AreEqual(10, result.Score);
    }

    [TestMethod]
    public async Task TestGradeFuzzyMatch_Incorrect()
    {
        var result = await _gradingService!.GradeAsync("This is wrong", "Answer", GradingStrategy.FuzzyMatch, 10, "Question");
        Assert.IsFalse(result.IsCorrect);
        Assert.AreEqual(0, result.Score);
    }

    [TestMethod]
    public async Task TestGradeAiEval_Success()
    {
        var aiResponse = "{ \"Score\": 8, \"Comment\": \"Good job\", \"IsCorrect\": true }";
        _mockOllamaService!.Setup(s => s.AskQuestion(It.IsAny<string>()))
            .ReturnsAsync(aiResponse);

        var result = await _gradingService!.GradeAsync("Student Answer", "Standard Answer", GradingStrategy.AiEval, 10, "Question", "Explanation");
        
        Assert.IsTrue(result.IsCorrect);
        Assert.AreEqual(8, result.Score);
        Assert.IsTrue(result.Comment.Contains("Good job"));
    }

    [TestMethod]
    public async Task TestGradeAiEval_WithMarkdown()
    {
        var aiResponse = "```json\n{ \"Score\": 5, \"Comment\": \"Partial\", \"IsCorrect\": false }\n```";
        _mockOllamaService!.Setup(s => s.AskQuestion(It.IsAny<string>()))
            .ReturnsAsync(aiResponse);

        var result = await _gradingService!.GradeAsync("Student Answer", "Standard Answer", GradingStrategy.AiEval, 10, "Question", "Explanation");
        
        Assert.IsFalse(result.IsCorrect);
        Assert.AreEqual(5, result.Score);
        Assert.IsTrue(result.Comment.Contains("Partial"));
    }

    [TestMethod]
    public async Task TestGradeAiEval_EmptyAnswer()
    {
        var result = await _gradingService!.GradeAsync("", "Standard Answer", GradingStrategy.AiEval, 10, "Question");
        
        Assert.IsFalse(result.IsCorrect);
        Assert.AreEqual(0, result.Score);
        Assert.AreEqual("No answer provided.", result.Comment);
    }
}
