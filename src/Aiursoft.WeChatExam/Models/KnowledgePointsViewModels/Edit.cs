using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.KnowledgePointsViewModels;

public class EditViewModel: UiStackLayoutViewModel
{
    public EditViewModel()
    {
        PageTitle = "Edit KnowledgePoint";
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Knowledge Point ID")]
    public Guid Id { get; set; }
    
    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(200, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(4000, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 父分类ID。若为 null，表示将分类移动到顶级。
    /// </summary>
    [Display(Name = "Parent Knowledge Point")]
    public Guid? ParentId { get; set; }

    /// <summary>
    /// 可选的父分类列表，用于下拉菜单
    /// </summary>
    public List<KnowledgePoint> AvailableParents { get; set; } = new();

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
}