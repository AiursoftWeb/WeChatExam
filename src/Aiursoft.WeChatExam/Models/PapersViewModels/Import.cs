using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aiursoft.WeChatExam.Models.PapersViewModels;

public enum ImportPaperKind { Mock, Real }

public class ImportViewModel : UiStackLayoutViewModel
{
    public ImportViewModel()
    {
        PageTitle = "Import Paper";
    }

    [Required, MaxLength(100000)]
    public string Source { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string SourceTagName { get; set; } = string.Empty;

    public Guid? TotalCategoryId { get; set; }
    public Guid? InstitutionId { get; set; }
    public int? TaxonomyId { get; set; }

    [Range(1, int.MaxValue)]
    public int TimeLimit { get; set; } = 180;

    [Required]
    public ImportPaperKind? Kind { get; set; }
    public bool AllowViewAnonymousely { get; set; }
    public string? ExtraTags { get; set; }

    public IEnumerable<SelectListItem> Courses { get; set; } = [];
    public IEnumerable<SelectListItem> Institutions { get; set; } = [];
    public List<QuestionCategory> QuestionCategories { get; set; } = [];
    public IEnumerable<SelectListItem> Taxonomies { get; set; } = [];
}

public class PaperImportExtractionRequest
{
    [Required, MaxLength(100000)] public string Source { get; set; } = string.Empty;
}

public class PaperImportPreview
{
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    public List<PaperImportQuestion> Questions { get; set; } = [];
}

public class PaperImportQuestion
{
    // Assigned by the reviewer, independently of the paper's category.
    public Guid? QuestionCategoryId { get; set; }
    [Required(AllowEmptyStrings = true), MaxLength(100)] public string OriginalNumber { get; set; } = string.Empty;
    public QuestionType QuestionType { get; set; }
    [Required, MaxLength(2000)] public string Content { get; set; } = string.Empty;
    public List<string> Options { get; set; } = [];
    [Required(AllowEmptyStrings = true), MaxLength(5000)] public string StandardAnswer { get; set; } = string.Empty;
    [Required(AllowEmptyStrings = true), MaxLength(3000)] public string Explanation { get; set; } = string.Empty;
    [Range(1, int.MaxValue)] public int Score { get; set; } = 10;
}

public class PaperImportSaveRequest : PaperImportPreview
{
    public Guid TaskId { get; set; }
    [Range(1, int.MaxValue)] public int TimeLimit { get; set; } = 180;
    [Required]
    public ImportPaperKind? Kind { get; set; }
    public bool AllowViewAnonymousely { get; set; }
    public Guid? TotalCategoryId { get; set; }
    public Guid? InstitutionId { get; set; }
    public int? TaxonomyId { get; set; }
    [MaxLength(255)] public string SourceTagName { get; set; } = string.Empty;
    public List<string> ExtraTags { get; set; } = [];
    public List<PaperImportTagConfirmation> Confirmations { get; set; } = [];
}

public class PaperImportTagConfirmation
{
    public int Id { get; set; }
    public int? TaxonomyId { get; set; }
}

public class PaperImportTagCheckRequest
{
    [Required, MaxLength(255)] public string SourceTagName { get; set; } = string.Empty;
    public List<string> ExtraTags { get; set; } = [];
    public int? TaxonomyId { get; set; }
}
