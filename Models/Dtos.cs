namespace AiEmailer.Api.Models;
 
    public record LoginRequest(string Email, string Password);
    public record LoginResponse(string Token, string Email, string Role, int ExpiresInMinutes);

    public record GenerateReplyRequest(string OriginalEmailBody, string Tone, string? SenderName);
    public record GenerateReplyResponse(string Draft);

    public record SummarizeRequest(string EmailBody, string Sender, string Subject);
    public record SummarizeResponse(string Summary, string[] ActionItems, string Priority);

 
 