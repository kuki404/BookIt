using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookIt.Application.Dtos;
using BookIt.Domain.Enums;
using Shouldly;

namespace BookIt.IntegrationTests;

/// <summary>
/// Covers the "look up my booking by reference code" endpoint (api/bookings/by-code/{code}) —
/// backs the unique index on Booking.ReferenceCode. Deliberately anonymous: a guest without an
/// account should be able to find their own booking with just the code from their confirmation
/// email, so these calls carry no bearer token.
/// </summary>
[Collection("Integration")]
public class BookingLookupByCodeTests(BookItWebApplicationFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetByReferenceCode_ForExistingBooking_ReturnsItAnonymously()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var token = await RegisterAndGetTokenAsync(client);
        var resourceId = await CreateDedicatedResourceAsync(client);
        var start = DateTime.UtcNow.AddDays(30).Date.AddHours(9);

        var createResponse = await PostBookingAsync(client, token, resourceId, start, start.AddHours(1));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<BookingDto>(Ct);

        var anonymousClient = factory.CreateClient();
        var lookup = await anonymousClient.GetAsync($"api/bookings/by-code/{created!.ReferenceCode}", Ct);

        lookup.EnsureSuccessStatusCode();
        var found = await lookup.Content.ReadFromJsonAsync<BookingDto>(Ct);
        found.ShouldNotBeNull();
        found!.Id.ShouldBe(created.Id);
        found.ReferenceCode.ShouldBe(created.ReferenceCode);
    }

    [Fact]
    public async Task GetByReferenceCode_ForUnknownCode_ReturnsNotFound()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var response = await client.GetAsync("api/bookings/by-code/BK-ZZZZZZ", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetByReferenceCode_IsCaseInsensitive()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var token = await RegisterAndGetTokenAsync(client);
        var resourceId = await CreateDedicatedResourceAsync(client);
        var start = DateTime.UtcNow.AddDays(31).Date.AddHours(9);

        var createResponse = await PostBookingAsync(client, token, resourceId, start, start.AddHours(1));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<BookingDto>(Ct);

        var lookup = await client.GetAsync($"api/bookings/by-code/{created!.ReferenceCode.ToLowerInvariant()}", Ct);

        lookup.EnsureSuccessStatusCode();
        var found = await lookup.Content.ReadFromJsonAsync<BookingDto>(Ct);
        found!.Id.ShouldBe(created.Id);
    }

    private static async Task<string> RegisterAndGetTokenAsync(HttpClient client)
    {
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var response = await client.PostAsJsonAsync("api/auth/register", new RegisterRequest(email, "Passw0rd!", "Test User"), Ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>(Ct);
        return body!.AccessToken;
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
