using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookIt.Application.Dtos;
using BookIt.Domain.Enums;
using Shouldly;

namespace BookIt.IntegrationTests;

/// <summary>
/// The availability endpoint backs the booking dialog's "show me what's already taken before I
/// submit" panel — it was previously wired up server-side but had no test coverage and no caller.
/// These prove the data shape the dialog now depends on.
/// </summary>
[Collection("Integration")]
public class AvailabilityTests(BookItWebApplicationFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetAvailability_ForDateWithNoBookings_ReturnsEmptySlots()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var resourceId = await CreateDedicatedResourceAsync(client);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));

        var response = await client.GetFromJsonAsync<AvailabilityResponse>(
            $"api/availability?resourceId={resourceId}&date={date:yyyy-MM-dd}", Ct);

        response.ShouldNotBeNull();
        response!.ResourceId.ShouldBe(resourceId);
        response.BookedSlots.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAvailability_AfterBooking_ReturnsTheBookedSlot()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var token = await RegisterAndGetTokenAsync(client);
        var resourceId = await CreateDedicatedResourceAsync(client);
        var start = DateTime.UtcNow.AddDays(11).Date.AddHours(10);
        var end = start.AddHours(1);

        var bookingResponse = await PostBookingAsync(client, token, resourceId, start, end);
        bookingResponse.EnsureSuccessStatusCode();

        var date = DateOnly.FromDateTime(start);
        var availability = await client.GetFromJsonAsync<AvailabilityResponse>(
            $"api/availability?resourceId={resourceId}&date={date:yyyy-MM-dd}", Ct);

        availability.ShouldNotBeNull();
        var slot = availability!.BookedSlots.ShouldHaveSingleItem();
        slot.StartUtc.ShouldBe(start);
        slot.EndUtc.ShouldBe(end);
    }

    [Fact]
    public async Task GetAvailabilityRange_AcrossMultipleDays_GroupsSlotsByDay()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var token = await RegisterAndGetTokenAsync(client);
        var resourceId = await CreateDedicatedResourceAsync(client);

        var day1Start = DateTime.UtcNow.AddDays(20).Date.AddHours(9);
        var day2Start = DateTime.UtcNow.AddDays(21).Date.AddHours(14);

        (await PostBookingAsync(client, token, resourceId, day1Start, day1Start.AddHours(1))).EnsureSuccessStatusCode();
        (await PostBookingAsync(client, token, resourceId, day2Start, day2Start.AddHours(1))).EnsureSuccessStatusCode();

        var startDate = DateOnly.FromDateTime(day1Start);
        var endDate = DateOnly.FromDateTime(day1Start.AddDays(6));

        var response = await client.GetFromJsonAsync<AvailabilityRangeResponse>(
            $"api/availability/range?resourceId={resourceId}&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}", Ct);

        response.ShouldNotBeNull();
        response!.Days.Count.ShouldBe(7);

        var day1 = response.Days.Single(d => d.Date == startDate);
        var day2 = response.Days.Single(d => d.Date == DateOnly.FromDateTime(day2Start));
        var freeDay = response.Days.Single(d => d.Date == startDate.AddDays(3));

        day1.BookedSlots.ShouldHaveSingleItem();
        day2.BookedSlots.ShouldHaveSingleItem();
        freeDay.BookedSlots.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAvailabilityRange_EndBeforeStart_ReturnsBadRequest()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var resourceId = await CreateDedicatedResourceAsync(client);
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var endDate = startDate.AddDays(-1);

        var response = await client.GetAsync(
            $"api/availability/range?resourceId={resourceId}&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
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
