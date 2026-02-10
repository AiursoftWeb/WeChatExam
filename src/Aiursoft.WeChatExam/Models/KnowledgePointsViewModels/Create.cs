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
    /// 父分类ID。若为 null，表示创建顶级分类。
    /// </summary>
    [Display(Name = "Parent Knowledge Point")]
    public Guid? ParentId { get; set; }

    /// <summary>
    /// 可选的父分类列表，用于下拉菜单
    /// </summary>
    public List<KnowledgePoint> AvailableParents { get; set; } = new();
}


