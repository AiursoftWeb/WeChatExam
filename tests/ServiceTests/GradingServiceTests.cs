using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Tests.TestServices;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class GradingServiceTests
{
    private StubOllamaService? _mockOllamaService;
    private StubGlobalSettingsService? _mockGlobalSettingsService;
    private GradingService? _gradingService;

    [TestInitialize]
    public void Setup()
    {
        _mockOllamaService = new StubOllamaService();
        _mockGlobalSettingsService = new StubGlobalSettingsService();
        
        // Mock the default grading prompt
        var defaultGradingPrompt = "Question: {0}\nStandard Answer: {1}\nExplanation: {2}\nStudent Answer: {3}\nMax Score: {4}";
        _mockGlobalSettingsService.GradingPrompt = defaultGradingPrompt;

        _gradingService = new GradingService(_mockOllamaService, _mockGlobalSettingsService);
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
        _mockOllamaService!.Response = aiResponse;

        var result = await _gradingService!.GradeAsync("Student Answer", "Standard Answer", GradingStrategy.AiEval, 10, "Question", "Explanation");
        
        Assert.IsTrue(result.IsCorrect);
        Assert.AreEqual(8, result.Score);
        Assert.IsTrue(result.Comment.Contains("Good job"));
    }

    [TestMethod]
    public async Task TestGradeAiEval_WithMarkdown()
    {
        var aiResponse = "```json\n{ \"Score\": 5, \"Comment\": \"Partial\", \"IsCorrect\": false }\n```";
        _mockOllamaService!.Response = aiResponse;

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
    [TestMethod]
    [DataRow("{}")]
    [DataRow("null")]
    [DataRow("not json")]
    [DataRow("{\"Score\":10,\"Comment\":\"Missing verdict\"}")]
    [DataRow("{\"Score\":-1,\"Comment\":\"Invalid score\",\"IsCorrect\":false}")]
    [DataRow("{\"Score\":11,\"Comment\":\"Invalid score\",\"IsCorrect\":true}")]
    [DataRow("{\"Score\":0,\"Comment\":\"Contradiction\",\"IsCorrect\":true}")]
    [DataRow("{\"Score\":10,\"Comment\":\"Contradiction\",\"IsCorrect\":false}")]
    [DataRow("{\"Score\":3,\"Comment\":\"\",\"IsCorrect\":false}")]
    [DataRow("{\"Score\":\"3\",\"Comment\":\"Wrong type\",\"IsCorrect\":false}")]
    public async Task InvalidAiResponseFailsInsteadOfInventingAnIncorrectGrade(string response)
    {
        _mockOllamaService!.Response = response;
        await Assert.ThrowsExactlyAsync<GradingUnavailableException>(() =>
            _gradingService!.GradeAsync("A reasoned answer", "Reference", GradingStrategy.AiEval, 10, "Question"));
    }

    [TestMethod]
    public async Task AiTransportFailureDoesNotBecomeAZeroScore()
    {
        _mockOllamaService!.Failure = new HttpRequestException("Unavailable");
        await Assert.ThrowsExactlyAsync<GradingUnavailableException>(() =>
            _gradingService!.GradeAsync("A reasoned answer", "Reference", GradingStrategy.AiEval, 10, "Question"));
    }

    [TestMethod]
    public async Task CoreCorrectnessDoesNotRequireFullMarksAndFeedbackIsSeparateFromExplanation()
    {
        _mockOllamaService!.Response = "{\"Score\":7,\"Comment\":\"The core reasoning is correct; supporting detail is missing.\",\"IsCorrect\":true}";
        var grade = await _gradingService!.GradeAsync("A reasoned answer", "Reference", GradingStrategy.AiEval, 10, "Question", "Static explanation");
        Assert.IsTrue(grade.IsCorrect);
        Assert.AreEqual(7, grade.Score);
        Assert.AreEqual("The core reasoning is correct; supporting detail is missing.", grade.Comment);
    }
}
