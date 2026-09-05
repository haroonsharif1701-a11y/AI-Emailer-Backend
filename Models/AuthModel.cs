namespace AiEmailer.Api.Models;

/// <summary>
/// Explicit int values because these cross the API boundary as-is — the
/// frontend switches on the number directly (per the login flow spec):
/// 1 = success, 0 = inactive account, 2 = invalid credentials, 3 = too many
/// failed attempts.
/// </summary>
public enum LoginStatus
{
    Inactive = 0,
    Success = 1,
    InvalidCredentials = 2,
    TooManyFailedAttempts = 3,
}

/// <summary>
/// Maps 1:1 to the columns returned by the auth_user stored procedure.
/// Internal to the auth flow only — PasswordHash is read here, verified in
/// AuthService, and never included in anything returned to the frontend.
/// </summary>
public class UserAuthRecord
{
    public int IdUser { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
    public Guid Uuqid { get; set; }
    public int IdRole { get; set; }
    public string? RoleName { get; set; }
    public int IdOrg { get; set; }
    public bool IsActive { get; set; }
    public int FailedAttempts { get; set; }
}

/// <summary>
/// What AuthService hands back to AuthController — enough to build both the
/// JWT and the session-facing LoginResponse, without the controller ever
/// touching a password hash or a SqlConnection directly.
/// </summary>
public record LoginResult(
    LoginStatus Status,
    int? IdUser = null,
    string? Username = null,
    string? FullName = null,
    Guid? Uuqid = null,
    int? IdRole = null,
    string? Role = null,
    int? IdOrg = null,
    int? LogStatus = null
);
