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

    [Required]
    public Guid Id { get; set; }
    
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(4000)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 父分类ID。若为 null，表示将分类移动到顶级。
    /// </summary>
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