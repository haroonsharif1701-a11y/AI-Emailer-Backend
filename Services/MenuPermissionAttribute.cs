using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AiEmailer.Api.Services;

// Multiple menu keys mean alternative entry points to the same operation.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class MenuPermissionAttribute(string action, params string[] menus) : Attribute, IAsyncActionFilter
{
    public string? IdProperty { get; set; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var access = await context.HttpContext.RequestServices.GetRequiredService<ICurrentAccessService>()
            .GetAsync(context.HttpContext.RequestAborted);
        var requiredAction = action;
        if (IdProperty is not null)
        {
            var argument = context.ActionArguments.Values.FirstOrDefault(value => value?.GetType().GetProperty(IdProperty) is not null);
            var id = argument?.GetType().GetProperty(IdProperty)?.GetValue(argument);
            if (id is not int number || number < 0)
            {
                context.Result = new BadRequestObjectResult(new { message = "A valid record id is required." });
                return;
            }
            requiredAction = number == 0 ? "add" : "update";
        }
        if (access is null || !menus.Any(menu => access.Allows(menu, requiredAction)))
        {
            context.Result = new ObjectResult(new { message = "You do not have permission to perform this action." }) { StatusCode = 403 };
            return;
        }
        await next();
    }
}
