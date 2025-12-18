using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.CategoriesViewModels;

public class DeleteViewModel : UiStackLayoutViewModel
{
    public DeleteViewModel()
    {
        PageTitle = "Delete Category";
    }

    public required Category Category { get; set; }

    /// <summary>
    /// 指示分类是否有子分类
    /// </summary>
    public bool HasChildren { get; set; }
}
