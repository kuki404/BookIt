using BookIt.Application.Abstractions;
using BookIt.Application.Dtos;
using BookIt.Infrastructure;
using BookIt.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<ApplicationUser> userManager,
    BookItDbContext db,
    ITokenService tokenService) : ControllerBase
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        if (await userManager.FindByEmailAsync(request.Email) is not null)
        {
            return Conflict(new { error = "An account with this email already exists." });
        }

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
            return ValidationProblem(string.Join(" ", createResult.Errors.Select(e => e.Description)));
        }

        await userManager.AddToRoleAsync(user, Roles.Customer);

        return Ok(await IssueTokensAsync(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
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

        if (storedToken is null || !storedToken.IsActive)
        {
            return Unauthorized(new { error = "Refresh token is invalid or expired." });
        }

        var user = await userManager.FindByIdAsync(storedToken.UserId.ToString());
        if (user is null)
        {
            return Unauthorized(new { error = "User no longer exists." });
        }

        // Rotate: the old token is immediately revoked so it can never be replayed, even if the
        // caller (or an attacker who captured it in transit) tries to reuse it.
        var response = await IssueTokensAsync(user);
        var newToken = await db.RefreshTokens.OrderByDescending(t => t.CreatedAtUtc).FirstAsync(t => t.UserId == user.Id);
        storedToken.Revoke(newToken.Id);
        await db.SaveChangesAsync();

        return Ok(response);
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(RefreshRequest request)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
        var storedToken = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (storedToken is { IsActive: true })
        {
            storedToken.Revoke();
            await db.SaveChangesAsync();
        }

        return NoContent();
    }

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user)
    {
        var roles = (await userManager.GetRolesAsync(user)).ToList();
        var accessToken = tokenService.CreateAccessToken(new TokenSubject(user.Id, user.Email!, user.DisplayName, roles));

        var (rawRefreshToken, refreshTokenHash) = tokenService.CreateRefreshToken();
        var refreshTokenEntity = Domain.Entities.RefreshToken.Create(user.Id, refreshTokenHash, RefreshTokenLifetime);
        db.RefreshTokens.Add(refreshTokenEntity);
        await db.SaveChangesAsync();

        return new AuthResponse(
            accessToken.Value,
            accessToken.ExpiresAtUtc,
            rawRefreshToken,
            refreshTokenEntity.ExpiresAtUtc,
            user.DisplayName,
            roles);
    }
}
