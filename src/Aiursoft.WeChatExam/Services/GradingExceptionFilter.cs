using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Aiursoft.WeChatExam.Services;

/// <summary>Keep grading failures retryable, without exposing provider errors or marking an answer.</summary>
public sealed class GradingExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not GradingUnavailableException) return;
        context.Result = new ObjectResult(new
        {
            Code = "grading_unavailable",
            Message = "Grading is temporarily unavailable. Your answer was not marked. Please try again."
        }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
        context.ExceptionHandled = true;
    }
}
