using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Services;

/// <summary>Protect live real-paper selections when deleting questions. Course moves remain valid.</summary>
public class RealPaperValidation(WeChatExamDbContext context, IStringLocalizer<RealPaperValidation> localizer)
{
    public async Task<string?> ValidateDeletionAsync(IEnumerable<Guid> questionIds)
    {
        var link = await context.RealPaperQuestions.AsNoTracking().Where(l => questionIds.Contains(l.QuestionId))
            .OrderBy(l => l.RealPaperId).ThenBy(l => l.QuestionId)
            .Select(l => new { l.RealPaper.Title, l.Question.Content }).FirstOrDefaultAsync();
        if (link == null) return null;
        var preview = link.Content.Length > 100 ? link.Content[..100] + "…" : link.Content;
        return string.Format(localizer["Question {0} is used by real paper {1}. Remove it from the paper before deleting it."].Value,
            preview, link.Title);
    }
}
