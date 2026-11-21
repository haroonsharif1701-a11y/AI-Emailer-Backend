namespace AiEmailer.Api.Services;

public static class ApplicationUrls
{
    public static string Origin(IConfiguration config, string key)
    {
        var value = config[key];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new InvalidOperationException($"Configure {key} as an HTTPS origin without a path, query, or credentials.");
        return uri.GetLeftPart(UriPartial.Authority);
    }

    public static string LoginCallback(IConfiguration config) =>
        string.IsNullOrWhiteSpace(config["FrontendUrl"]) ? config["Authentication:ExternalRedirectUri"]
            ?? "http://localhost:5500/app/login-success.html" : Origin(config, "FrontendUrl") + "/login-success.html";

    public static string GmailCallback(IConfiguration config) =>
        string.IsNullOrWhiteSpace(config["BaseUrl"]) ? config["Mailbox:Gmail:RedirectUri"]
            ?? throw new InvalidOperationException("Configure BaseUrl.") : Origin(config, "BaseUrl") + "/api/mailbox/gmail/callback";
}
