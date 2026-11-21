using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AiEmailer.Api.Services;

public sealed class MailboxException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class MailboxExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var error = context.Exception;
        if (error is OperationCanceledException && context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            context.Result = new StatusCodeResult(499);
            context.ExceptionHandled = true;
            return;
        }
        var status = error is MailboxException mailbox ? mailbox.Status :
            error is OperationCanceledException { InnerException: TimeoutException } ? 504 :
            error is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } ? 409 :
            error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or System.Text.RegularExpressions.RegexMatchTimeoutException ? 502 : 0;
        if (status == 0) return;
        context.Result = new ObjectResult(new { message = error is MailboxException ? error.Message :
            status == 409 ? "This mailbox is already connected to another user in your organization." :
            status == 504 ? "Gmail took too long to respond. Try again." : "Mail service is unavailable. Try again." }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
