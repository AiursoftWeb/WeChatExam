using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.PaperCollectionsViewModels;

public class EditViewModel : UiStackLayoutViewModel
{
    public EditViewModel() { PageTitle = "Edit Paper Collection"; }
    public Guid? Id { get; set; }
    public Guid Revision { get; set; }
    [Required, MaxLength(255), Display(Name = "Collection name")]
    public string Name { get; set; } = string.Empty;
    [Display(Name = "Display order")]
    public int OrderIndex { get; set; }
    [Display(Name = "Show in the Mini Program")]
    public bool IsVisible { get; set; }
    [Display(Name = "Archive this collection")]
    public bool IsArchived { get; set; }
    public List<Guid> PaperIds { get; set; } = [];
    public List<CollectionPaper> Papers { get; set; } = [];
    public int? LegacyTagId { get; set; }
    public int SharedQuestionCount { get; set; }
}
