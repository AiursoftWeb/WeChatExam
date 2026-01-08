using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Models.HomeViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.Management;

[LimitPerMin]
public class HomeController : Controller
{
    private readonly IOllamaService _ollamaService;

    public HomeController(IOllamaService ollamaService)
    {
        _ollamaService = ollamaService;
    }

    public IActionResult Index()
    {
        return this.SimpleView(new IndexViewModel());
    }

    [HttpGet]
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Home",
        CascadedLinksIcon = "home",
        CascadedLinksOrder = 1,
        LinkText = "Ask Ollama",
        LinkOrder = 2)]
    public IActionResult AskOllama()
    {
        return this.StackView(new AskOllamaViewModel
        {
            PageTitle = "Ask Ollama"
        });
    }

    [HttpPost]
    public async Task<IActionResult> AskOllama(string question, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return this.StackView(new AskOllamaViewModel
            {
                PageTitle = "Ask Ollama",
                Question = question
            });
        }

        var answer = await _ollamaService.AskQuestion(question, cancellationToken);

        return this.StackView(new AskOllamaViewModel
        {
            PageTitle = "Ask Ollama",
            Question = question,
            Answer = answer
        });
    }
}
