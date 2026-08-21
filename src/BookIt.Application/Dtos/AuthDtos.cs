using System.ComponentModel.DataAnnotations;

namespace BookIt.Application.Dtos;

public record RegisterRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    // Matches the actual Identity password policy (Program.cs's AddIdentityCore only overrides
    // RequiredLength and RequireNonAlphanumeric — the digit/lowercase/uppercase requirements are
    // Identity's untouched defaults). The client's helper text used to promise only "8 characters",
    // so a password like "password1" passed here and failed server-side with no useful message —
    // this attribute gives the same fast, specific feedback client and server agree on.
    [Required, MinLength(8), MaxLength(100)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9]).+$",
        ErrorMessage = "Password must include a lowercase letter, an uppercase letter, and a digit.")]
    string Password,
    [Required, MaxLength(100)] string DisplayName);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record RefreshRequest([Required] string RefreshToken);

public record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    string DisplayName,
    IReadOnlyList<string> Roles);
