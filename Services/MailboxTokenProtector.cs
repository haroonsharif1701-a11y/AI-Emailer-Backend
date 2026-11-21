using AiEmailer.Api.Models;
using Microsoft.AspNetCore.DataProtection;

namespace AiEmailer.Api.Services;

public sealed class MailboxTokenProtector(IDataProtectionProvider provider)
{
    private IDataProtector For(MailboxOwner owner) => provider.CreateProtector("AiEmailer.Mailbox.v1", owner.OrgId.ToString(), owner.UserId.ToString());
    public string Protect(MailboxOwner owner, string token) => For(owner).Protect(token);
    public string Unprotect(MailboxOwner owner, string token) => For(owner).Unprotect(token);
}
