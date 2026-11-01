using System.ComponentModel.DataAnnotations;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public class MistakeQueryDto
{
    [Range(0, int.MaxValue)]
    public int Offset { get; set; }

    [Range(1, 50)]
    public int Count { get; set; } = 20;
}

public record MistakePageDto<T>(int Total, int Offset, int Count, List<T> Items);

public record MistakeSummaryDto(Guid Id, string Content, QuestionType QuestionType, MistakeSource Source,
    string? SourcePaperTitle, DateTime CreationTime, int MistakeCount);

public record MistakeDetailsDto(Guid Id, string Content, QuestionType QuestionType, string Metadata,
    string UserAnswer, string StandardAnswer, string Explanation, int Score, int MaxScore, string GradingComment,
    DateTime CreationTime, MistakeSource Source, PracticeType? PracticeType, Guid? SourceQuestionId,
    Guid? SourceQuestionSnapshotId, Guid? SourcePaperId, Guid? SourcePaperSnapshotId,
    int? SourcePaperVersion, string? SourcePaperTitle);
