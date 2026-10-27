using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.KnowledgeCategoriesViewModels;

public class EditViewModel: UiStackLayoutViewModel
{
    public EditViewModel()
    {
        PageTitle = "Edit Knowledge Category";
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Category ID")]
    public Guid Id { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(200, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    public Guid TotalCategoryId { get; set; }
    public string TotalCategoryTitle { get; set; } = string.Empty;

    [Display(Name = "Is Free")]
    public bool IsFree { get; set; }

    /// <summary>
    /// The parent directory, or null for a top-level directory within the same course.
    /// </summary>
    [Display(Name = "Parent Category")]
    public Guid? ParentKnowledgeCategoryId { get; set; }

    /// <summary>
    /// Available parent directories.
    /// </summary>
    public List<KnowledgeCategory> AvailableParents { get; set; } = new();
}
