using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookIt.Application.Dtos;
using BookIt.Domain.Enums;

namespace BookIt.IntegrationTests;

public class AuthAndBookingsTests(BookItWebApplicationFactory factory) : IClassFixture<BookItWebApplicationFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Register_WithNewEmail_ReturnsTokens()
    {
        var request = new RegisterRequest($"user-{Guid.NewGuid():N}@test.local", "Passw0rd!", "Test User");

        var response = await client.PostAsJsonAsync("api/auth/register", request);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrEmpty(body!.AccessToken));
        Assert.Contains("Customer", body.Roles);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var email = $"user-{Guid.NewGuid():N}@test.local";
        await client.PostAsJsonAsync("api/auth/register", new RegisterRequest(email, "Passw0rd!", "Test User"));

        var response = await client.PostAsJsonAsync("api/auth/login", new LoginRequest(email, "WrongPassword!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetResources_Anonymous_ReturnsSeededResources()
    {
        var response = await client.GetAsync("api/resources");

        response.EnsureSuccessStatusCode();
        var resources = await response.Content.ReadFromJsonAsync<List<ResourceDto>>();
        Assert.NotNull(resources);
        Assert.NotEmpty(resources!);
    }

    [Fact]
    public async Task CreateBooking_WithoutAuthentication_ReturnsUnauthorized()
    {
        var resourceId = (await GetFirstResourceIdAsync());
        var start = DateTime.UtcNow.AddDays(1);
        var request = new CreateBookingRequest(resourceId, start, start.AddHours(1), null);

        var response = await client.PostAsJsonAsync("api/bookings", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateBooking_ThenOverlappingBooking_ReturnsBadRequest()
    {
        var token = await RegisterAndGetTokenAsync();
        // A dedicated resource per test — not the shared seeded one — so this test's booking can
        // never collide with another test (or a previous run's leftover data) on the same
        // resource/time window.
        var resourceId = await CreateDedicatedResourceAsync();
        var start = DateTime.UtcNow.AddDays(2);

        var first = await PostBookingAsync(token, resourceId, start, start.AddHours(1));
        first.EnsureSuccessStatusCode();

        var overlapping = await PostBookingAsync(token, resourceId, start.AddMinutes(30), start.AddMinutes(90));

        Assert.Equal(HttpStatusCode.BadRequest, overlapping.StatusCode);
    }

    [Fact]
    public async Task ConfirmBooking_AsNonAdmin_ReturnsForbidden()
    {
        var token = await RegisterAndGetTokenAsync();
        var resourceId = await CreateDedicatedResourceAsync();
        var start = DateTime.UtcNow.AddDays(3);

        var createResponse = await PostBookingAsync(token, resourceId, start, start.AddHours(1));
        createResponse.EnsureSuccessStatusCode();
        var booking = await createResponse.Content.ReadFromJsonAsync<BookingDto>();

        var request = new HttpRequestMessage(HttpMethod.Post, $"api/bookings/{booking!.Id}/confirm");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var confirmResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, confirmResponse.StatusCode);
    }

    private async Task<string> RegisterAndGetTokenAsync()
    {
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var response = await client.PostAsJsonAsync("api/auth/register", new RegisterRequest(email, "Passw0rd!", "Test User"));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return body!.AccessToken;
    }

    private async Task<Guid> GetFirstResourceIdAsync()
    {
        var resources = await client.GetFromJsonAsync<List<ResourceDto>>("api/resources");
        return resources!.First().Id;
    }

    private async Task<Guid> CreateDedicatedResourceAsync()
    {
        var loginResponse = await client.PostAsJsonAsync("api/auth/login", new LoginRequest("admin@bookit.local", "Admin123!"));
        loginResponse.EnsureSuccessStatusCode();
        var adminAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        var request = new HttpRequestMessage(HttpMethod.Post, "api/resources")
        {
            Content = JsonContent.Create(new CreateResourceRequest($"Test Room {Guid.NewGuid():N}", ResourceType.Room, 4, null))
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
