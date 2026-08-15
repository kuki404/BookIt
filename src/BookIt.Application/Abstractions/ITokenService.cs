namespace BookIt.Application.Abstractions;

public record TokenSubject(Guid UserId, string Email, string DisplayName, IReadOnlyList<string> Roles);

public record AccessToken(string Value, DateTime ExpiresAtUtc);

/// <summary>
/// Lives in Application as an abstraction so the service layer can issue tokens without
/// depending on the JWT/Identity packages directly — those concerns are implemented in
/// Infrastructure, which knows about ASP.NET Core Identity and System.IdentityModel.Tokens.Jwt.
/// </summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(TokenSubject subject);

    /// <summary>Generates a cryptographically random refresh token value (returned to the client) and its hash (stored in the DB).</summary>
    (string RawToken, string TokenHash) CreateRefreshToken();

    string HashRefreshToken(string rawToken);
}
