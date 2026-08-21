using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookIt.Application.Common;
using BookIt.Application.Dtos;
using Shouldly;

namespace BookIt.IntegrationTests;

/// <summary>Covers the security hardening added in the retrofit: lockout, refresh-token reuse detection, and server-enforced pagination limits.</summary>
[Collection("Integration")]
public class SecurityTests(BookItWebApplicationFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_AfterFiveFailedAttempts_LocksAccountEvenWithCorrectPassword()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@test.local";
        const string correctPassword = "Passw0rd!";
        await client.PostAsJsonAsync("api/auth/register", new RegisterRequest(email, correctPassword, "Test User"), Ct);

        // Identity's default lockout threshold (Program.cs: MaxFailedAccessAttempts = 5) is
        // per-account, counted by SignInManager.CheckPasswordSignInAsync — five wrong passwords
        // in a row should lock the account regardless of what's tried next.
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("api/auth/login", new LoginRequest(email, "WrongPassword!"), Ct);
        }

        var response = await client.PostAsJsonAsync("api/auth/login", new LoginRequest(email, correctPassword), Ct);

        response.StatusCode.ShouldBe((HttpStatusCode)423); // 423 Locked
    }

    [Fact]
    public async Task Refresh_WithAlreadyUsedToken_RevokesEntireSessionFamily()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var registerResponse = await client.PostAsJsonAsync("api/auth/register", new RegisterRequest(email, "Passw0rd!", "Test User"), Ct);
        var original = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>(Ct);

        // Rotate once — this is the legitimate path, "original" is now revoked and replaced.
        var firstRefresh = await client.PostAsJsonAsync("api/auth/refresh", new RefreshRequest(original!.RefreshToken), Ct);
        firstRefresh.EnsureSuccessStatusCode();
        var rotated = await firstRefresh.Content.ReadFromJsonAsync<AuthResponse>(Ct);

        // Reuse the original (already-rotated-away) token — simulates a captured/stolen token
        // being replayed after the legitimate client already moved on.
        var reuseAttempt = await client.PostAsJsonAsync("api/auth/refresh", new RefreshRequest(original.RefreshToken), Ct);
        reuseAttempt.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The whole family — including the token issued by the legitimate rotation above — must
        // now be dead too, not just the replayed one.
        var rotatedNowRevoked = await client.PostAsJsonAsync("api/auth/refresh", new RefreshRequest(rotated!.RefreshToken), Ct);
        rotatedNowRevoked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetResources_WithPageSizeAboveMaximum_ReturnsBadRequest()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        // PagedRequest.PageSize is capped with [Range(1, 100)] — this isn't just a documented
        // default, the API must actually reject a client asking for everything at once.
        var response = await client.GetAsync("api/resources?page=1&pageSize=1000", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetAllBookings_AsCustomer_ReturnsForbidden()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var registerResponse = await client.PostAsJsonAsync("api/auth/register", new RegisterRequest(email, "Passw0rd!", "Test User"), Ct);
        var auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>(Ct);

        var request = new HttpRequestMessage(HttpMethod.Get, "api/bookings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        var response = await client.SendAsync(request, Ct);

        // AdminOnly policy — a plain Customer token is authenticated (not 401) but not authorized
        // (403) for the admin-wide booking list.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
