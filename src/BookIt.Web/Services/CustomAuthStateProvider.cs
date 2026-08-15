using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace BookIt.Web.Services;

public class CustomAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly AuthSession session;

    public CustomAuthStateProvider(AuthSession session)
    {
        this.session = session;
        session.Changed += OnChanged;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var auth = session.Current;
        if (auth is null)
        {
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, JwtClaimsHelper.GetUserId(auth.AccessToken).ToString()),
            new(ClaimTypes.Name, auth.DisplayName)
        };
        claims.AddRange(auth.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, authenticationType: "jwt");
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    private void OnChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    public void Dispose() => session.Changed -= OnChanged;
}
