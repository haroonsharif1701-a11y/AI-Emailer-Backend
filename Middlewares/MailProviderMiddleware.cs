using AiEmailer.Api.Services;

namespace AiEmailer.Api.Middlewares;

public sealed class MailProviderMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, MailProviderSettings settings)
    {
        var path = context.Request.Path;
        if ((!settings.Gmail && (path.StartsWithSegments("/signin-google") || path.StartsWithSegments("/api/mailbox"))) ||
            (!settings.Outlook && path.StartsWithSegments("/signin-oidc")))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { message = "This provider is disabled." });
            return;
        }
        await next(context);
    }
}
