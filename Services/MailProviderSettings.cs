namespace AiEmailer.Api.Services;

public sealed class MailProviderSettings
{
    public bool Gmail { get; set; }
    public bool Outlook { get; set; }

    public void Validate()
    {
        if (Gmail && Outlook) throw new InvalidOperationException("Enable at most one MailProvider.");
        if (Outlook) throw new InvalidOperationException("Outlook mailbox support is not implemented yet. Disable Outlook.");
    }

    public bool AllowsLogin(string? provider) => provider switch
    {
        "Google" => Gmail,
        "EntraID" => Outlook,
        _ => false
    };
}
