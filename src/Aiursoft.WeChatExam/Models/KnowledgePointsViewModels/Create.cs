using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.KnowledgePointsViewModels;

public class CreateViewModel: UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create KnowledgePoint";
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(200, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(4000, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Content")]
    public string Content { get; set; } = string.Empty;


    /// <summary>
    /// 选中的关联题目分类
    /// </summary>
    public List<Guid> SelectedCategoryIds { get; set; } = new();

    /// <summary>
    /// 可选的题目分类
    /// </summary>
    public List<Category> AvailableCategories { get; set; } = new();

    /// <summary>
    /// 选中的关联题目
    /// </summary>
    public List<Guid> SelectedQuestionIds { get; set; } = new();

    /// <summary>
    /// 可选的题目
    /// </summary>
    public List<Question> AvailableQuestions { get; set; } = new();

    public IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> QuestionTypeOptions { get; set; } = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
}


