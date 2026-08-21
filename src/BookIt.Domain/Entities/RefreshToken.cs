namespace BookIt.Domain.Entities;

/// <summary>
/// Stores only the SHA-256 hash of the refresh token, never the raw value — mirrors how ASP.NET
/// Core Identity stores password hashes. Tokens are rotated on every use: a new row is created
/// and this one is marked revoked/replaced, so a stolen-and-reused token is detectable.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }

    public bool IsActiveAt(DateTime nowUtc) => RevokedAtUtc is null && nowUtc < ExpiresAtUtc;

    private RefreshToken()
    {
        // EF Core materialization constructor.
    }

    public static RefreshToken Create(Guid userId, string tokenHash, TimeSpan lifetime, DateTime nowUtc)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(lifetime)
        };
    }

    public void Revoke(DateTime nowUtc, Guid? replacedByTokenId = null)
    {
        RevokedAtUtc = nowUtc;
        ReplacedByTokenId = replacedByTokenId;
    }
}
