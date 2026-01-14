using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.ExtractViewModels;

public class ExtractIndexViewModel: UiStackLayoutViewModel
{
    [Required]
    [Display(Name = "Material")]
    public string Material { get; set; } =string.Empty;

    [Required]
    [Display(Name = "System Prompt")]
    public string SystemPrompt { get; set; } = string.Empty;
}

public class ExtractEditViewModel: UiStackLayoutViewModel
{
    [Required]
    public string JsonContent { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
    
    public string OriginalMaterial { get; set; } = string.Empty;
}

public class ExtractPreviewViewModel: UiStackLayoutViewModel
{
    public string JsonContent { get; set; } = string.Empty;

    public List<ExtractedKnowledgePoint> Data { get; set; } = new();

    [Required]
    [Display(Name = "Target Category")]
    public Guid CategoryId { get; set; }

    public SelectList? Categories { get; set; }
}
