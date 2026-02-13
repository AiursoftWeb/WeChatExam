using Aiursoft.WeChatExam.Services;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class ExamStatusCalculatorTests
{
    [TestMethod]
    public void TestGetStatusUpcoming()
    {
        var startTime = DateTime.UtcNow.AddDays(1);
        var endTime = DateTime.UtcNow.AddDays(2);
        var status = ExamStatusCalculator.GetStatus(startTime, endTime);
        Assert.AreEqual("Upcoming", status);
    }

    [TestMethod]
    public void TestGetStatusActive()
    {
        var startTime = DateTime.UtcNow.AddDays(-1);
        var endTime = DateTime.UtcNow.AddDays(1);
        var status = ExamStatusCalculator.GetStatus(startTime, endTime);
        Assert.AreEqual("Active", status);
    }

    [TestMethod]
    public void TestGetStatusEnded()
    {
        var startTime = DateTime.UtcNow.AddDays(-2);
        var endTime = DateTime.UtcNow.AddDays(-1);
        var status = ExamStatusCalculator.GetStatus(startTime, endTime);
        Assert.AreEqual("Ended", status);
    }

    [TestMethod]
    public void TestGetStatusBadgeClass()
    {
        Assert.AreEqual("bg-secondary", ExamStatusCalculator.GetStatusBadgeClass("Upcoming"));
        Assert.AreEqual("bg-success", ExamStatusCalculator.GetStatusBadgeClass("Active"));
        Assert.AreEqual("bg-dark", ExamStatusCalculator.GetStatusBadgeClass("Ended"));
        Assert.AreEqual("bg-secondary", ExamStatusCalculator.GetStatusBadgeClass("Unknown"));
    }
}
