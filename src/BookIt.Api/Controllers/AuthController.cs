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
    ITokenService tokenService,
    TimeProvider timeProvider) : ControllerBase
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
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
            // A rejected password is not an enumeration oracle — telling the user which rule their
            // password failed reveals nothing about whether the email is already registered. Only
            // collapse to the generic message when a duplicate-email error is among the failures
            // (mixed in with, say, a password error too — the generic message wins so a duplicate
            // check can never be inferred from response specificity).
            var isDuplicateEmail = createResult.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");
            var detail = isDuplicateEmail
                ? "Could not create an account with the provided details."
                : string.Join(" ", createResult.Errors.Select(e => e.Description));

            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Registration failed.", detail: detail);
        }

        await userManager.AddToRoleAsync(user, Roles.Customer);

        return Ok(await IssueTokensAsync(user, cancellationToken));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
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

        return Ok(await IssueTokensAsync(user, cancellationToken));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
        var storedToken = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

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
                .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAtUtc, timeProvider.GetUtcNow().UtcDateTime), cancellationToken);

            return Unauthorized(new { error = "Refresh token has already been used. All sessions were revoked." });
        }

        if (!storedToken.IsActiveAt(timeProvider.GetUtcNow().UtcDateTime))
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
        var (response, newTokenEntity) = await IssueTokensWithEntityAsync(user, cancellationToken);
        storedToken.Revoke(timeProvider.GetUtcNow().UtcDateTime, newTokenEntity.Id);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(response);
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(RefreshRequest request, CancellationToken cancellationToken)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);

        // ExecuteUpdateAsync: a single UPDATE, no SELECT-then-save round trip for what's really a
        // one-column write, and it's a no-op (0 rows affected) if the token doesn't exist/is
        // already revoked — no need to branch on that here.
        await db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAtUtc, timeProvider.GetUtcNow().UtcDateTime), cancellationToken);

        return NoContent();
    }

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        (await IssueTokensWithEntityAsync(user, cancellationToken)).Response;

    private async Task<(AuthResponse Response, Domain.Entities.RefreshToken Entity)> IssueTokensWithEntityAsync(
        ApplicationUser user, CancellationToken cancellationToken)
    {
        var roles = (await userManager.GetRolesAsync(user)).ToList();
        var accessToken = tokenService.CreateAccessToken(new TokenSubject(user.Id, user.Email!, user.DisplayName, roles));

        var (rawRefreshToken, refreshTokenHash) = tokenService.CreateRefreshToken();
        var refreshTokenEntity = Domain.Entities.RefreshToken.Create(user.Id, refreshTokenHash, RefreshTokenLifetime, timeProvider.GetUtcNow().UtcDateTime);
        db.RefreshTokens.Add(refreshTokenEntity);
        await db.SaveChangesAsync(cancellationToken);

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
