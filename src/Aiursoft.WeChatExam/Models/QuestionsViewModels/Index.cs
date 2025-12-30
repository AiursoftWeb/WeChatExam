using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.QuestionsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Questions";
    }

    public List<Question> Questions { get; set; } = new();

    public Guid? SelectedCategoryId { get; set; }

    public Category? SelectedCategory { get; set; }

    public List<Category> Categories { get; set; } = new();
}
