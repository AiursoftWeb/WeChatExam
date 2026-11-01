using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Stages an independent copy after authorized grading. The caller saves it together
/// with the existing answer history in one SaveChanges transaction.
/// </summary>
public static class MistakeRecording
{
    public static void Record(WeChatExamDbContext context, string userId, Question question,
        string userAnswer, GradingResult grade, PracticeType? practiceType, DateTime creationTime)
    {
        // The server verdict governs collection: a correct core answer may earn partial points.
        if (grade.IsCorrect || string.IsNullOrWhiteSpace(userAnswer)) return;
        context.MistakeRecords.Add(new MistakeRecord
        {
            UserId = userId, Source = MistakeSource.QuestionBank, SourceQuestionId = question.Id,
            Content = question.Content, QuestionType = question.QuestionType, Metadata = question.Metadata,
            UserAnswer = userAnswer, StandardAnswer = question.StandardAnswer, Explanation = question.Explanation,
            Score = grade.Score, MaxScore = 10, GradingComment = grade.Comment,
            PracticeType = practiceType, CreationTime = creationTime
        });
    }

    public static void Record(WeChatExamDbContext context, string? userId, QuestionSnapshot question,
        PaperSnapshot paper, string userAnswer, GradingResult grade, PracticeType? practiceType, DateTime creationTime)
    {
        if (userId == null || grade.IsCorrect || string.IsNullOrWhiteSpace(userAnswer)) return;
        // Copy the actual published version, even when the original question no longer exists.
        context.MistakeRecords.Add(new MistakeRecord
        {
            UserId = userId, Source = paper.RealPaperId.HasValue ? MistakeSource.RealPaper : MistakeSource.MockPaper,
            SourceQuestionId = question.OriginalQuestionId, SourceQuestionSnapshotId = question.Id,
            SourcePaperId = paper.RealPaperId ?? paper.MockPaperId, SourcePaperSnapshotId = paper.Id,
            SourcePaperVersion = paper.Version, SourcePaperTitle = paper.Title,
            Content = question.Content, QuestionType = question.QuestionType, Metadata = question.Metadata,
            UserAnswer = userAnswer, StandardAnswer = question.StandardAnswer, Explanation = question.Explanation,
            Score = grade.Score, MaxScore = question.Score, GradingComment = grade.Comment,
            PracticeType = practiceType, CreationTime = creationTime
        });
    }
}
