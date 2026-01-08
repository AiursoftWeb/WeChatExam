using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public class ExamDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string PaperTitle { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int DurationMinutes { get; set; }
    public int AllowedAttempts { get; set; }
    public int MyAttemptCount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class ExamRecordDto
{
    public Guid Id { get; set; }
    public string ExamTitle { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? SubmitTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalScore { get; set; }
    public bool HasReview { get; set; }
}

public class ExamSessionDto
{
    public Guid RecordId { get; set; }
    public Guid PaperSnapshotId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public Dictionary<Guid, string> Answers { get; set; } = new();
}

public class SubmitAnswerDto
{
    [Required]
    public Guid QuestionSnapshotId { get; set; }
    public string Answer { get; set; } = string.Empty;
}

public class ExamRecordDetailDto
{
    public Guid Id { get; set; }
    public int Score { get; set; }
    public string TeacherComment { get; set; } = string.Empty;
    public List<AnswerResultDto> Answers { get; set; } = new();
}

public class AnswerResultDto
{
    public Guid? QuestionSnapshotId { get; set; }
    public string UserAnswer { get; set; } = string.Empty;
    public int Score { get; set; }
    public string? StandardAnswer { get; set; }
    public string GradingResult { get; set; } = string.Empty;
}
