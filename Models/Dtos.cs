namespace AiEmailer.Api.Models;
 
    public record LoginRequest(string Email, string Password);

    // Extended from the original 4-field version: Status carries the
    // 0/1/2/3 login outcome, and Uuqid/IdRole/Role/IdOrg are the session
    // fields the frontend needs once logged in. All the new fields are
    // nullable since they're only populated when Status == Success.
    public record LoginResponse(
        int Status,
        string? Token,
        string? Username,
        string? FullName,
        Guid? Uuqid,
        int? IdRole,
        string? Role,
        int? IdOrg,
        int ExpiresInMinutes
    );

    public record GenerateReplyRequest(string OriginalEmailBody, string Tone, string? SenderName);
    public record GenerateReplyResponse(string Draft);

    public record SummarizeRequest(string EmailBody, string Sender, string Subject);
    public record SummarizeResponse(string Summary, string[] ActionItems, string Priority);

 
 