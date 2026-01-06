using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public static class ExamStatusCalculator
{
    public static string GetStatus(DateTime startTime, DateTime endTime)
    {
        var now = DateTime.UtcNow;
        if (now < startTime)
        {
            return "Upcoming";
        }
        else if (now > endTime)
        {
            return "Ended";
        }
        else
        {
            return "Active";
        }
    }

    public static string GetStatusBadgeClass(string status)
    {
        return status switch
        {
            "Upcoming" => "bg-secondary",
            "Active" => "bg-success",
            "Ended" => "bg-dark",
            _ => "bg-secondary"
        };
    }
}
