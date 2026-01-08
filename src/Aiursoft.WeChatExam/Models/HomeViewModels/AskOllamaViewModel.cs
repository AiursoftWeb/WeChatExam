using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.HomeViewModels;

public class AskOllamaViewModel : UiStackLayoutViewModel
{
    public string? Question { get; set; }
    public string? Answer { get; set; }
}
