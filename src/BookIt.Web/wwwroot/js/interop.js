// Thin wrapper around the browser Clipboard API — no external JS library needed for something
// this small. Called from Blazor via IJSRuntime.InvokeVoidAsync("copyToClipboard", text).
window.copyToClipboard = async (text) => {
    await navigator.clipboard.writeText(text);
};
