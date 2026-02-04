using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.QuestionImportViewModels;

/// <summary>
/// Index page model - JSON input and QuestionType selection
/// </summary>
public class QuestionImportIndexViewModel : UiStackLayoutViewModel
{
    public QuestionImportIndexViewModel()
    {
        PageTitle = "Question Import";
    }

    [Required]
    [Display(Name = "JSON Content")]
    public string JsonContent { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Question Type")]
    public QuestionType SelectedQuestionType { get; set; }

    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Parsed question item from JSON input
/// </summary>
public class ImportedQuestionItem
{
    public string Type { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
    public string Answer { get; set; } = string.Empty;
    public string Analysis { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
}

/// <summary>
/// Preview page model - shows parsed questions before import
/// </summary>
public class QuestionImportPreviewViewModel : UiStackLayoutViewModel
{
    public QuestionImportPreviewViewModel()
    {
        PageTitle = "Preview Import";
    }

    /// <summary>
    /// Hidden field to preserve JSON content for re-edit
    /// </summary>
    public string JsonContent { get; set; } = string.Empty;

    /// <summary>
    /// User-selected question type (overrides JSON Type field)
    /// </summary>
    [Required]
    [Display(Name = "Question Type")]
    public QuestionType SelectedQuestionType { get; set; }

    /// <summary>
    /// Parsed question items for preview display
    /// </summary>
    public List<ImportedQuestionItem> ParsedQuestions { get; set; } = new();

    public string? ErrorMessage { get; set; }
}
