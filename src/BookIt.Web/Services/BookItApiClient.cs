using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookIt.Application.Common;
using BookIt.Application.Dtos;

namespace BookIt.Web.Services;

/// <summary>Thin typed HttpClient wrapper around BookIt.Api. Attaches the access token to every
/// call and transparently refreshes it once on a 401 before giving up.</summary>
public class BookItApiClient(HttpClient http, AuthSession session)
{
    public async Task<(bool Success, string? Error)> RegisterAsync(RegisterRequest request)
    {
        var response = await http.PostAsJsonAsync("api/auth/register", request);
        if (!response.IsSuccessStatusCode)
        {
            return (false, await ReadErrorAsync(response));
        }

        await session.SetAsync((await response.Content.ReadFromJsonAsync<AuthResponse>())!);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> LoginAsync(LoginRequest request)
    {
        var response = await http.PostAsJsonAsync("api/auth/login", request);
        if (!response.IsSuccessStatusCode)
        {
            return (false, await ReadErrorAsync(response));
        }

        await session.SetAsync((await response.Content.ReadFromJsonAsync<AuthResponse>())!);
        return (true, null);
    }

    public Task LogoutAsync() => session.ClearAsync();

    public Task<PagedResult<ResourceDto>?> GetResourcesAsync(bool includeInactive = false, int page = 1, int pageSize = 50) =>
        SendAsync<PagedResult<ResourceDto>>(HttpMethod.Get, $"api/resources?includeInactive={includeInactive}&page={page}&pageSize={pageSize}");

    public Task<ResourceDto?> CreateResourceAsync(CreateResourceRequest request) =>
        SendAsync<ResourceDto>(HttpMethod.Post, "api/resources", request);

    public Task<ResourceDto?> UpdateResourceAsync(Guid id, UpdateResourceRequest request) =>
        SendAsync<ResourceDto>(HttpMethod.Put, $"api/resources/{id}", request);

    public Task<HttpResponseMessage> DeactivateResourceAsync(Guid id) =>
        SendRawAsync(HttpMethod.Delete, $"api/resources/{id}");

    public Task<AvailabilityResponse?> GetAvailabilityAsync(Guid resourceId, DateOnly date) =>
        SendAsync<AvailabilityResponse>(HttpMethod.Get, $"api/availability?resourceId={resourceId}&date={date:yyyy-MM-dd}");

    public async Task<(BookingDto? Booking, string? Error)> CreateBookingAsync(CreateBookingRequest request)
    {
        var response = await SendRawAsync(HttpMethod.Post, "api/bookings", request);
        if (!response.IsSuccessStatusCode)
        {
            return (null, await ReadErrorAsync(response));
        }

        return (await response.Content.ReadFromJsonAsync<BookingDto>(), null);
    }

    public Task<PagedResult<BookingDto>?> GetMyBookingsAsync(int page = 1, int pageSize = 50) =>
        SendAsync<PagedResult<BookingDto>>(HttpMethod.Get, $"api/bookings/mine?page={page}&pageSize={pageSize}");

    public Task<PagedResult<BookingDto>?> GetAllBookingsAsync(int page = 1, int pageSize = 50) =>
        SendAsync<PagedResult<BookingDto>>(HttpMethod.Get, $"api/bookings?page={page}&pageSize={pageSize}");

    public Task<BookingDto?> ConfirmBookingAsync(Guid id) =>
        SendAsync<BookingDto>(HttpMethod.Post, $"api/bookings/{id}/confirm");

    public Task<BookingDto?> CheckInBookingAsync(Guid id) =>
        SendAsync<BookingDto>(HttpMethod.Post, $"api/bookings/{id}/check-in");

    public Task<BookingDto?> CompleteBookingAsync(Guid id) =>
        SendAsync<BookingDto>(HttpMethod.Post, $"api/bookings/{id}/complete");

    public async Task<(BookingDto? Booking, string? Error)> CancelBookingAsync(Guid id, string? reason)
    {
        var response = await SendRawAsync(HttpMethod.Post, $"api/bookings/{id}/cancel", new CancelBookingRequest(reason));
        if (!response.IsSuccessStatusCode)
        {
            return (null, await ReadErrorAsync(response));
        }

        return (await response.Content.ReadFromJsonAsync<BookingDto>(), null);
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string url, object? body = null)
    {
        var response = await SendRawAsync(method, url, body);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>() : default;
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string url, object? body = null)
    {
        var response = await SendOnceAsync(method, url, body);

        if (response.StatusCode == HttpStatusCode.Unauthorized && session.Current is not null)
        {
            var refreshed = await TryRefreshAsync();
            if (refreshed)
            {
                response = await SendOnceAsync(method, url, body);
            }
        }

        return response;
    }

    private Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (session.Current is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Current.AccessToken);
        }

        return http.SendAsync(request);
    }

    private async Task<bool> TryRefreshAsync()
    {
        if (session.Current is null)
        {
            return false;
        }

        var response = await http.PostAsJsonAsync("api/auth/refresh", new RefreshRequest(session.Current.RefreshToken));
        if (!response.IsSuccessStatusCode)
        {
            await session.ClearAsync();
            return false;
        }

        await session.SetAsync((await response.Content.ReadFromJsonAsync<AuthResponse>())!);
        return true;
    }

    /// <summary>
    /// The API returns three different shapes for "the request was rejected", and this used to
    /// only understand one of them: `{ "error": "..." }` (the shape most controllers use), leaving
    /// DataAnnotations' ValidationProblemDetails (an `errors` dictionary) and plain ProblemDetails
    /// (`detail`/`title`, what AuthController.Register actually returns) to fall through silently
    /// to a useless "Request failed with status 400." — which is exactly what a user seeing "your
    /// password was rejected" with no reason saw. Checked in order: `errors` (first validation
    /// message) -> `detail` -> `title` -> `error`.
    /// </summary>
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
            if (problem is null)
            {
                return $"Request failed with status {(int)response.StatusCode}.";
            }

            if (problem.TryGetValue("errors", out var errors) && errors.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    if (field.Value.ValueKind == System.Text.Json.JsonValueKind.Array && field.Value.GetArrayLength() > 0)
                    {
                        return field.Value[0].GetString() ?? "Request failed.";
                    }
                }
            }

            if (problem.TryGetValue("detail", out var detail) && detail.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return detail.GetString() ?? "Request failed.";
            }

            if (problem.TryGetValue("title", out var title) && title.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return title.GetString() ?? "Request failed.";
            }

            if (problem.TryGetValue("error", out var error) && error.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return error.GetString() ?? "Request failed.";
            }
        }
        catch
        {
            // Fall through to the generic message below — the body wasn't JSON we recognize.
        }

        return $"Request failed with status {(int)response.StatusCode}.";
    }
}
