using BookIt.Web.Components;
using BookIt.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// This Web app never actually signs a cookie in — auth state comes from CustomAuthStateProvider,
// backed by the JWT the Api issued. A default scheme still has to be registered because
// AuthorizeRouteView runs through ASP.NET Core's own authorization middleware during the initial
// (static) render before the interactive circuit takes over.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie();
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();

var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5098";
builder.Services.AddHttpClient<BookItApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl));

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

app.Run();
