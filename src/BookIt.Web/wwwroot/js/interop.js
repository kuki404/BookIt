// Thin wrapper around the browser Clipboard API — no external JS library needed for something
// this small. Called from Blazor via IJSRuntime.InvokeVoidAsync("copyToClipboard", text).
window.copyToClipboard = async (text) => {
    await navigator.clipboard.writeText(text);
};

// Read the OS/browser color-scheme preference once, on first render, when the user hasn't picked
// a theme explicitly yet (see ThemeService). No MudBlazor-version-specific API involved.
window.getSystemDarkMode = () => window.matchMedia('(prefers-color-scheme: dark)').matches;
