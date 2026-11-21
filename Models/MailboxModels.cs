using System.ComponentModel.DataAnnotations;

namespace AiEmailer.Api.Models;

public record MailboxOwner(int UserId, int OrgId);
public record MailboxConnection(MailboxOwner Owner, string AccountId, string Email, string RefreshToken,
    string Scope, Guid Version, string? HistoryId = null, DateTime? SyncedAt = null, bool ReconnectRequired = false);
public record MailboxAuthorization(MailboxOwner Owner, Guid SessionId, string Verifier);
public record MailboxStatus(bool Connected, string? Email, bool ReconnectRequired, DateTime? SyncedAt);
public record MailAttachment(string Id, string FileName, string ContentType, long Size);
public record MailMessage(string Id, string ThreadId, string From, string To, string Subject, DateTimeOffset ReceivedAt,
    string Snippet, string BodyText, string[] Labels, MailAttachment[] Attachments, string? InternetMessageId = null, string? References = null);
public record MailPage(IReadOnlyList<MailMessage> Messages, string? NextPageToken);
public record MailFile(string FileName, byte[] Content);
public record SendMailRequest([Required, EmailAddress, StringLength(320)] string To,
    [Required, StringLength(998)] string Subject, [Required, StringLength(100000)] string Body, string? ReplyToMessageId = null);
public record MailActionRequest([Required, RegularExpression("^(read|unread|archive)$")] string Action);
public record MailSyncResult(string HistoryId, bool Reset, int Changes);
