using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;

namespace AiEmailer.Api.Services;

public sealed class RbacExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not SqlException sql || sql.Number is < 51000 or > 51009) return;
        var status = sql.Number switch { 51001 => 403, 51002 => 409, 51003 => 404, _ => 400 };
        context.Result = new ObjectResult(new { message = sql.Message }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
