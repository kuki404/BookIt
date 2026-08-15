using BookIt.Application.Dtos;

namespace BookIt.Web.Services;

/// <summary>
/// Holds the current user's tokens in memory for the lifetime of their Blazor Server circuit.
/// Blazor Server keeps one persistent connection/scope per user, so — unlike Blazor WebAssembly —
/// there's no need to touch browser storage (localStorage/cookies) to keep this alive.
/// </summary>
public class AuthSession
{
    public AuthResponse? Current { get; private set; }

    public event Action? Changed;

    public void Set(AuthResponse response)
    {
        Current = response;
        Changed?.Invoke();
    }

    public void Clear()
    {
        Current = null;
        Changed?.Invoke();
    }
}
