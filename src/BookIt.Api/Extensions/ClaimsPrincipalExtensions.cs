using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace BookIt.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("No user id claim present on the principal.");

        return Guid.Parse(value);
    }
}
