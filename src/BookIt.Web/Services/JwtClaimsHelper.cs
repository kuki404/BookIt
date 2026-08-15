using System.Text;
using System.Text.Json;

namespace BookIt.Web.Services;

/// <summary>
/// Decodes a JWT payload for display/UI purposes only (e.g. reading the user id claim to show
/// "your bookings"). This never validates the signature — the Api is the only party that trusts
/// the token; it re-validates it on every request regardless of what the UI decodes here.
/// </summary>
public static class JwtClaimsHelper
{
    public static Dictionary<string, JsonElement> DecodePayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3)
        {
            return [];
        }

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));

        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
    }

    public static Guid GetUserId(string jwt)
    {
        var claims = DecodePayload(jwt);
        return claims.TryGetValue("sub", out var value) ? Guid.Parse(value.GetString()!) : Guid.Empty;
    }
}
