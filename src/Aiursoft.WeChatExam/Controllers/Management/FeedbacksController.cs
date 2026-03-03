using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.FeedbacksViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.Management;

[LimitPerMin]
[Authorize(Policy = AppPermissionNames.CanReadFeedbacks)]
public class FeedbacksController(IFeedbackService feedbackService) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Feedback",
        CascadedLinksIcon = "comment",
        CascadedLinksOrder = 9997,
        LinkText = "User Feedbacks",
        LinkOrder = 1)]
    public async Task<IActionResult> Index(int page = 1, FeedbackStatus? status = null)
    {
        const int pageSize = 20;
        var (items, totalCount) = await feedbackService.SearchFeedbacksAsync(page, pageSize, status);
        
        return this.StackView(new IndexViewModel
        {
            Feedbacks = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            StatusFilter = status
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditFeedbacks)]
    public async Task<IActionResult> Process(int id)
    {
        await feedbackService.UpdateFeedbackStatusAsync(id, FeedbackStatus.Processed);
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AppPermissionNames.CanDeleteFeedbacks)]
    public async Task<IActionResult> Delete(int id)
    {
        var feedback = await feedbackService.GetFeedbackByIdAsync(id);
        if (feedback == null)
        {
            return NotFound();
        }
        return this.StackView(new DeleteViewModel
        {
            Feedback = feedback
        });
    }

    [HttpPost]
    [ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanDeleteFeedbacks)]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        await feedbackService.DeleteFeedbackAsync(id);
        return RedirectToAction(nameof(Index));
    }
}
