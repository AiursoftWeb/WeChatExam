using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.ExtractViewModels;

public abstract class ExtractDestinationViewModel : UiStackLayoutViewModel
{
    public Guid? CategoryId { get; set; }

    public Guid? QuestionCategoryId { get; set; }

    public SelectList? Categories { get; set; }
    public SelectList? QuestionCategories { get; set; }
}

public class ExtractIndexViewModel: ExtractDestinationViewModel
{
    public ExtractIndexViewModel()
    {
        PageTitle = "Extract Knowledge";
    }
    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Material")]
    public string Material { get; set; } =string.Empty;

    [Display(Name = "System Prompt")]
    public string? SystemPrompt { get; set; }
}

public class ExtractEditViewModel: UiStackLayoutViewModel
{
    public ExtractEditViewModel()
    {
        PageTitle = "Edit Extracted JSON";
    }
    
    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "JSON Content")]
    public string JsonContent { get; set; } = string.Empty;

    public Guid? CategoryId { get; set; }
    public Guid? QuestionCategoryId { get; set; }

    public string? ErrorMessage { get; set; }
    
    public string OriginalMaterial { get; set; } = string.Empty;
}

public class ExtractPreviewViewModel: ExtractDestinationViewModel
{
    public ExtractPreviewViewModel()
    {
        PageTitle = "Preview Data";
    }
    public string JsonContent { get; set; } = string.Empty;

    public List<ExtractedKnowledgePoint> Data { get; set; } = new();

    public string OriginalMaterial { get; set; } = string.Empty;
}
