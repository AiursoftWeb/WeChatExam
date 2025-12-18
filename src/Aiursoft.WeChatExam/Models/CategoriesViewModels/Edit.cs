using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.CategoriesViewModels;

public class EditViewModel: UiStackLayoutViewModel
{
    public EditViewModel()
    {
        PageTitle = "Edit Category";
    }

    [Required]
    public Guid Id { get; set; }
    
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 父分类ID。若为 null，表示将分类移动到顶级。
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// 可选的父分类列表，用于下拉菜单
    /// </summary>
    public List<Category> AvailableParents { get; set; } = new();
}