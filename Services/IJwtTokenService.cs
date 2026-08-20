namespace AiEmailer.Api.Services;

public interface IJwtTokenService
{
    // idOrg added: required for any endpoint that needs to scope data to the
    // caller's own organization (e.g. listing users) — previously there was
    // no way to know which org a token's holder belonged to.
    string CreateToken(string userId, string email, string role, string idOrg);
}
