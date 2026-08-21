using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookIt.Application.Common;
using BookIt.Application.Dtos;
using BookIt.Domain.Enums;
using Shouldly;

namespace BookIt.IntegrationTests;

[Collection("Integration")]
public class AuthAndBookingsTests(BookItWebApplicationFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_WithNewEmail_ReturnsTokens()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var request = new RegisterRequest($"user-{Guid.NewGuid():N}@test.local", "Passw0rd!", "Test User");

        var response = await client.PostAsJsonAsync("api/auth/register", request, Ct);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>(Ct);
        body.ShouldNotBeNull();
        body!.AccessToken.ShouldNotBeNullOrEmpty();
        body.Roles.ShouldContain("Customer");
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@test.local";
        await client.PostAsJsonAsync("api/auth/register", new RegisterRequest(email, "Passw0rd!", "Test User"), Ct);

        var response = await client.PostAsJsonAsync("api/auth/login", new LoginRequest(email, "WrongPassword!"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetResources_Anonymous_ReturnsSeededResources()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var response = await client.GetAsync("api/resources", Ct);

        response.EnsureSuccessStatusCode();
        var resources = await response.Content.ReadFromJsonAsync<PagedResult<ResourceDto>>(Ct);
        resources.ShouldNotBeNull();
        resources!.Items.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task CreateBooking_WithoutAuthentication_ReturnsUnauthorized()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var resourceId = await GetFirstResourceIdAsync(client);
        var start = DateTime.UtcNow.AddDays(1);
        var request = new CreateBookingRequest(resourceId, start, start.AddHours(1), null);

        var response = await client.PostAsJsonAsync("api/bookings", request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateBooking_ThenOverlappingBooking_ReturnsBadRequest()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var token = await RegisterAndGetTokenAsync(client);
        // A dedicated resource per test — not the shared seeded one — so this test's booking can
        // never collide with another test (or a previous run's leftover data) on the same
        // resource/time window. Capacity 1: a single overlap must be enough to reject.
        var resourceId = await CreateDedicatedResourceAsync(client, capacity: 1);
        var start = DateTime.UtcNow.AddDays(2);

        var first = await PostBookingAsync(client, token, resourceId, start, start.AddHours(1));
        first.EnsureSuccessStatusCode();

        var overlapping = await PostBookingAsync(client, token, resourceId, start.AddMinutes(30), start.AddMinutes(90));

        overlapping.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateBooking_UpToCapacity_AllSucceedAndTheNextOneIsRejected()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var token = await RegisterAndGetTokenAsync(client);
        // Resource.Capacity used to be validated and displayed but silently ignored by the
        // overlap check, so a room with capacity 4 behaved exactly like capacity 1. Prove
        // overlapping bookings up to capacity all succeed, and only the one past it is rejected.
        var resourceId = await CreateDedicatedResourceAsync(client, capacity: 4);
        var start = DateTime.UtcNow.AddDays(4);

        for (var i = 0; i < 4; i++)
        {
            var response = await PostBookingAsync(client, token, resourceId, start, start.AddHours(1));
            response.EnsureSuccessStatusCode();
        }

        var overCapacity = await PostBookingAsync(client, token, resourceId, start, start.AddHours(1));

        overCapacity.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConfirmBooking_AsNonAdmin_ReturnsForbidden()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var token = await RegisterAndGetTokenAsync(client);
        var resourceId = await CreateDedicatedResourceAsync(client);
        var start = DateTime.UtcNow.AddDays(3);

        var createResponse = await PostBookingAsync(client, token, resourceId, start, start.AddHours(1));
        createResponse.EnsureSuccessStatusCode();
        var booking = await createResponse.Content.ReadFromJsonAsync<BookingDto>(Ct);

        var request = new HttpRequestMessage(HttpMethod.Post, $"api/bookings/{booking!.Id}/confirm");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var confirmResponse = await client.SendAsync(request, Ct);

        confirmResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<string> RegisterAndGetTokenAsync(HttpClient client)
    {
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var response = await client.PostAsJsonAsync("api/auth/register", new RegisterRequest(email, "Passw0rd!", "Test User"), Ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>(Ct);
        return body!.AccessToken;
    }

    private static async Task<Guid> GetFirstResourceIdAsync(HttpClient client)
    {
        var resources = await client.GetFromJsonAsync<PagedResult<ResourceDto>>("api/resources", Ct);
        return resources!.Items.First().Id;
    }

    private static async Task<Guid> CreateDedicatedResourceAsync(HttpClient client, int capacity = 4)
    {
        var loginResponse = await client.PostAsJsonAsync("api/auth/login", new LoginRequest("admin@bookit.local", "Admin123!"), Ct);
        loginResponse.EnsureSuccessStatusCode();
        var adminAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>(Ct);

        var request = new HttpRequestMessage(HttpMethod.Post, "api/resources")
        {
            Content = JsonContent.Create(new CreateResourceRequest($"Test Room {Guid.NewGuid():N}", ResourceType.Room, capacity, null))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth!.AccessToken);

        var response = await client.SendAsync(request, Ct);
        response.EnsureSuccessStatusCode();
        var resource = await response.Content.ReadFromJsonAsync<ResourceDto>(Ct);
        return resource!.Id;
    }

    private static Task<HttpResponseMessage> PostBookingAsync(HttpClient client, string token, Guid resourceId, DateTime startUtc, DateTime endUtc)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/bookings")
        {
            Content = JsonContent.Create(new CreateBookingRequest(resourceId, startUtc, endUtc, null))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request, Ct);
    }
}
