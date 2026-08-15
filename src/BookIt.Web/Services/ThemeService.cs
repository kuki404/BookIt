using System.Security.Cryptography;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace BookIt.Web.Services;

/// <summary>
/// Dark/light preference: explicit choice wins and is remembered (ProtectedLocalStorage — a UI
/// preference, not sensitive, so browser-persisted rather than session-only); with no explicit
/// choice yet, the OS/browser preference is used instead (read once via JS interop on first render).
/// </summary>
public class ThemeService(ProtectedLocalStorage storage)
{
    private const string StorageKey = "bookit-dark-mode";

    public bool IsDarkMode { get; private set; }
    public event Action? Changed;

    public async Task InitializeAsync(bool systemPrefersDark)
    {
        // Unlike ProtectedSessionStorage, this value outlives a server restart (it's browser
        // localStorage) — so it's the more likely of the two to be encrypted under a Data
        // Protection key the app no longer has after a redeploy with no persisted key ring. An
        // undecryptable stored preference just means "no preference recorded", not a crash.
        try
        {
            var stored = await storage.GetAsync<bool>(StorageKey);
            IsDarkMode = stored.Success ? stored.Value : systemPrefersDark;
        }
        catch (CryptographicException)
        {
            IsDarkMode = systemPrefersDark;
        }
    }

    public async Task SetAsync(bool isDarkMode)
    {
        IsDarkMode = isDarkMode;
        await storage.SetAsync(StorageKey, isDarkMode);
        Changed?.Invoke();
    }
}
