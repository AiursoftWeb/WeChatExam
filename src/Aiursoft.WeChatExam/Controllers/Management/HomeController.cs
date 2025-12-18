using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Models.HomeViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.Management;

[LimitPerMin]
public class HomeController : Controller
{
    public IActionResult Index()
    {
        return this.SimpleView(new IndexViewModel());
    }
}
