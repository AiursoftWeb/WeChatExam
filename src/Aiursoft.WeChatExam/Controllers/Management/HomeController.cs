using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Models.HomeViewModels;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.BackgroundJobs;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.Management;

[LimitPerMin]
public class HomeController : Controller
{
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
    public IActionResult AskOllama(string question, [FromServices] BackgroundJobQueue backgroundJobQueue)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return this.StackView(new AskOllamaViewModel
            {
                PageTitle = "Ask Ollama",
                Question = question
            });
        }

        backgroundJobQueue.QueueWithDependency<IOllamaService>(
            queueName: "OllamaChat",
            jobName: $"Ask Ollama: {question}",
            job: async (ollamaService) => await ollamaService.AskQuestion(question)
        );

        return RedirectToAction("Index", "Jobs");
    }
}
