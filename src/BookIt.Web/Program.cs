using BookIt.Web.Components;
using BookIt.Web.Mapping;
using BookIt.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor.Services;

// DTO -> Web ViewModel mappings (display-only fields like chip colors) — registered once,
// process-wide, before any request uses `.Adapt<T>()`.
MapsterConfig.Configure();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// This Web app never actually signs a cookie in — auth state comes from CustomAuthStateProvider,
// backed by the JWT the Api issued. A default scheme still has to be registered because
// AuthorizeRouteView runs through ASP.NET Core's own authorization middleware during the initial
// (static) render before the interactive circuit takes over. LoginPath must point at this app's
// real "/login" route — a direct/bookmarked visit to an [Authorize] page (e.g. /MyBookings) while
// logged out is challenged here, and ASP.NET Core's default "/Account/Login" 404s since no such
// page exists in this app.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => options.LoginPath = "/login");
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
builder.Services.AddScoped<ThemeService>();

var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5098";
builder.Services.AddHttpClient<BookItApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl));

// Liveness only — this app has no database of its own to check readiness against.
builder.Services.AddHealthChecks();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();
