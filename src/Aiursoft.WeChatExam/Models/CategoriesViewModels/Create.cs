using System.ComponentModel.DataAnnotations;
using Aiursoft.CSTools.Attributes;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.CategoriesViewModels;

public class CreateViewModel: UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create Category";
    }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 父分类ID。若为 null，表示创建顶级分类。
    /// </summary>
    public Guid? ParentId { get; set; }
}

