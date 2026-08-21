using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookIt.Application.Dtos;
using BookIt.Domain.Enums;

namespace BookIt.IntegrationTests;

/// <summary>
/// The availability endpoint backs the booking dialog's "show me what's already taken before I
/// submit" panel — it was previously wired up server-side but had no test coverage and no caller.
/// These prove the data shape the dialog now depends on.
/// </summary>
[Collection("Integration")]
public class AvailabilityTests(BookItWebApplicationFactory factory)
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task GetAvailability_ForDateWithNoBookings_ReturnsEmptySlots()
    {
        var resourceId = await CreateDedicatedResourceAsync();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));

        var response = await client.GetFromJsonAsync<AvailabilityResponse>(
            $"api/availability?resourceId={resourceId}&date={date:yyyy-MM-dd}");

        Assert.NotNull(response);
        Assert.Equal(resourceId, response!.ResourceId);
        Assert.Empty(response.BookedSlots);
    }

    [Fact]
    public async Task GetAvailability_AfterBooking_ReturnsTheBookedSlot()
    {
        var token = await RegisterAndGetTokenAsync();
        var resourceId = await CreateDedicatedResourceAsync();
        var start = DateTime.UtcNow.AddDays(11).Date.AddHours(10);
        var end = start.AddHours(1);

        var bookingResponse = await PostBookingAsync(token, resourceId, start, end);
        bookingResponse.EnsureSuccessStatusCode();

        var date = DateOnly.FromDateTime(start);
        var availability = await client.GetFromJsonAsync<AvailabilityResponse>(
            $"api/availability?resourceId={resourceId}&date={date:yyyy-MM-dd}");

        Assert.NotNull(availability);
        var slot = Assert.Single(availability!.BookedSlots);
        Assert.Equal(start, slot.StartUtc);
        Assert.Equal(end, slot.EndUtc);
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
