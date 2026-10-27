using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.KnowledgeCategoriesViewModels;

public class CreateViewModel: UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create Knowledge Category";
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(200, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Total Category")]
    public Guid TotalCategoryId { get; set; }

    public List<TotalCategory> TotalCategories { get; set; } = new();

    [Display(Name = "Is Free")]
    public bool IsFree { get; set; }

    /// <summary>
    /// The parent directory, or null for a top-level directory within the course.
    /// </summary>
    [Display(Name = "Parent Category")]
    public Guid? ParentKnowledgeCategoryId { get; set; }

    /// <summary>
    /// Available parent directories.
    /// </summary>
    public List<KnowledgeCategory> AvailableParents { get; set; } = new();
}


