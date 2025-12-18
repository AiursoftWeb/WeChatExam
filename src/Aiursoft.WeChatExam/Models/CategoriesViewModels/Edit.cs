using System.ComponentModel.DataAnnotations;
using Aiursoft.CSTools.Attributes;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.CategoriesViewModels;

public class EditViewModel: UiStackLayoutViewModel
{
    public EditViewModel()
    {
        PageTitle = "Edit Category";
    }
    
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 父分类ID。若为 null，表示将分类移动到顶级。
    /// </summary>
    public Guid? ParentId { get; set; }
}