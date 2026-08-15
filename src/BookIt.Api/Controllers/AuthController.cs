using BookIt.Application.Abstractions;
using BookIt.Application.Dtos;
using BookIt.Infrastructure;
using BookIt.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous] // the API is authenticated-by-default (see FallbackPolicy in Program.cs); auth itself must stay reachable
[EnableRateLimiting("auth")] // login/register/refresh are the highest-value brute-force targets — stricter than the global per-IP limit
public class AuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    BookItDbContext db,
    ITokenService tokenService) : ControllerBase
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            // Deliberately generic: distinguishing "email already registered" from "weak password"
            // would let an attacker enumerate registered accounts. The auth-specific rate limiter
            // (Program.cs) is the primary defense against brute-forcing this endpoint either way.
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Registration failed.",
                detail: "Could not create an account with the provided details.");
        }

        await userManager.AddToRoleAsync(user, Roles.Customer);

        return Ok(await IssueTokensAsync(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            // Same 401 + message as a wrong password below — an "unknown email" response would
            // let an attacker enumerate accounts one guess at a time.
            return Unauthorized(new { error = "Invalid email or password." });
        }

        // CheckPasswordSignInAsync (not UserManager.CheckPasswordAsync) is what actually counts
        // failed attempts and locks the account out — a plain password check has no brute-force
        // protection at all.
        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return Problem(statusCode: StatusCodes.Status423Locked, title: "Account locked.",
                detail: "Too many failed attempts. Try again later.");
        }

        if (!result.Succeeded)
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }

        return Ok(await IssueTokensAsync(user));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
        var storedToken = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (storedToken is null)
        {
            return Unauthorized(new { error = "Refresh token is invalid or expired." });
        }

        if (storedToken.RevokedAtUtc is not null)
        {
            // Reuse of an already-revoked token means someone replayed a captured/stolen token —
            // the legitimate rotation already moved past it. Treat this as a compromise: kill
            // every active session for the account, not just this one token (OWASP/OAuth 2.0
            // Security BCP "refresh token reuse detection").
            await db.RefreshTokens
                .Where(t => t.UserId == storedToken.UserId && t.RevokedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow));

            return Unauthorized(new { error = "Refresh token has already been used. All sessions were revoked." });
        }

        if (!storedToken.IsActive)
        {
            return Unauthorized(new { error = "Refresh token is invalid or expired." });
        }

        var user = await userManager.FindByIdAsync(storedToken.UserId.ToString());
        if (user is null)
        {
            return Unauthorized(new { error = "User no longer exists." });
        }

        // Rotate: issue a new pair, then link the old row to the new one and revoke it — captured
        // directly from IssueTokensAsync's return value instead of re-querying "the newest token
        // for this user", which would race under concurrent refresh calls.
        var (response, newTokenEntity) = await IssueTokensWithEntityAsync(user);
        storedToken.Revoke(newTokenEntity.Id);
        await db.SaveChangesAsync();

        return Ok(response);
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(RefreshRequest request)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);

        // ExecuteUpdateAsync: a single UPDATE, no SELECT-then-save round trip for what's really a
        // one-column write, and it's a no-op (0 rows affected) if the token doesn't exist/is
        // already revoked — no need to branch on that here.
        await db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow));

        return NoContent();
    }

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user) => (await IssueTokensWithEntityAsync(user)).Response;

    private async Task<(AuthResponse Response, Domain.Entities.RefreshToken Entity)> IssueTokensWithEntityAsync(ApplicationUser user)
    {
        var roles = (await userManager.GetRolesAsync(user)).ToList();
        var accessToken = tokenService.CreateAccessToken(new TokenSubject(user.Id, user.Email!, user.DisplayName, roles));

        var (rawRefreshToken, refreshTokenHash) = tokenService.CreateRefreshToken();
        var refreshTokenEntity = Domain.Entities.RefreshToken.Create(user.Id, refreshTokenHash, RefreshTokenLifetime);
        db.RefreshTokens.Add(refreshTokenEntity);
        await db.SaveChangesAsync();

        var response = new AuthResponse(
            accessToken.Value,
            accessToken.ExpiresAtUtc,
            rawRefreshToken,
            refreshTokenEntity.ExpiresAtUtc,
            user.DisplayName,
            roles);

        return (response, refreshTokenEntity);
    }
}
