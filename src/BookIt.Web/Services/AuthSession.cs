using BookIt.Application.Dtos;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace BookIt.Web.Services;

/// <summary>
/// Holds the current user's tokens for the lifetime of their Blazor Server circuit, and mirrors
/// them into ProtectedSessionStorage (browser sessionStorage, encrypted via ASP.NET Core Data
/// Protection) so a page refresh — which tears down and rebuilds the circuit — doesn't silently
/// log the user out. Session-, not local-storage: the token should not outlive the browser tab.
/// </summary>
public class AuthSession(ProtectedSessionStorage storage)
{
    private const string StorageKey = "bookit-auth";

    public AuthResponse? Current { get; private set; }

    public event Action? Changed;

    /// <summary>Call once per circuit, before relying on <see cref="Current"/>, to pick up a session that survived a refresh.</summary>
    public async Task RestoreAsync()
    {
        var result = await storage.GetAsync<AuthResponse>(StorageKey);
        if (result is { Success: true, Value.AccessTokenExpiresAtUtc: var expiry } && expiry > DateTime.UtcNow)
        {
            Current = result.Value;
            Changed?.Invoke();
        }
    }

    public async Task SetAsync(AuthResponse response)
    {
        Current = response;
        await storage.SetAsync(StorageKey, response);
        Changed?.Invoke();
    }

    public async Task ClearAsync()
    {
        Current = null;
        await storage.DeleteAsync(StorageKey);
        Changed?.Invoke();
    }
}
