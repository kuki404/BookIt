using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookIt.Application.Dtos;
using BookIt.Domain.Enums;

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
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task GetByReferenceCode_ForExistingBooking_ReturnsItAnonymously()
    {
        var token = await RegisterAndGetTokenAsync();
        var resourceId = await CreateDedicatedResourceAsync();
        var start = DateTime.UtcNow.AddDays(30).Date.AddHours(9);

        var createResponse = await PostBookingAsync(token, resourceId, start, start.AddHours(1));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<BookingDto>();

        var anonymousClient = factory.CreateClient();
        var lookup = await anonymousClient.GetAsync($"api/bookings/by-code/{created!.ReferenceCode}");

        lookup.EnsureSuccessStatusCode();
        var found = await lookup.Content.ReadFromJsonAsync<BookingDto>();
        Assert.NotNull(found);
        Assert.Equal(created.Id, found!.Id);
        Assert.Equal(created.ReferenceCode, found.ReferenceCode);
    }

    [Fact]
    public async Task GetByReferenceCode_ForUnknownCode_ReturnsNotFound()
    {
        var response = await client.GetAsync("api/bookings/by-code/BK-ZZZZZZ");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetByReferenceCode_IsCaseInsensitive()
    {
        var token = await RegisterAndGetTokenAsync();
        var resourceId = await CreateDedicatedResourceAsync();
        var start = DateTime.UtcNow.AddDays(31).Date.AddHours(9);

        var createResponse = await PostBookingAsync(token, resourceId, start, start.AddHours(1));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<BookingDto>();

        var lookup = await client.GetAsync($"api/bookings/by-code/{created!.ReferenceCode.ToLowerInvariant()}");

        lookup.EnsureSuccessStatusCode();
        var found = await lookup.Content.ReadFromJsonAsync<BookingDto>();
        Assert.Equal(created.Id, found!.Id);
    }

    private async Task<string> RegisterAndGetTokenAsync()
    {
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var response = await client.PostAsJsonAsync("api/auth/register", new RegisterRequest(email, "Passw0rd!", "Test User"));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return body!.AccessToken;
    }

    private async Task<Guid> CreateDedicatedResourceAsync(int capacity = 4)
    {
        var loginResponse = await client.PostAsJsonAsync("api/auth/login", new LoginRequest("admin@bookit.local", "Admin123!"));
        loginResponse.EnsureSuccessStatusCode();
        var adminAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        var request = new HttpRequestMessage(HttpMethod.Post, "api/resources")
        {
            Content = JsonContent.Create(new CreateResourceRequest($"Test Room {Guid.NewGuid():N}", ResourceType.Room, capacity, null))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth!.AccessToken);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var resource = await response.Content.ReadFromJsonAsync<ResourceDto>();
        return resource!.Id;
    }

    private Task<HttpResponseMessage> PostBookingAsync(string token, Guid resourceId, DateTime startUtc, DateTime endUtc)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/bookings")
        {
            Content = JsonContent.Create(new CreateBookingRequest(resourceId, startUtc, endUtc, null))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }
}
