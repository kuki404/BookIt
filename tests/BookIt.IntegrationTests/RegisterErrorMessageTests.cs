using System.Net;
using System.Net.Http.Json;
using BookIt.Application.Dtos;

namespace BookIt.IntegrationTests;

/// <summary>
/// Regression coverage for a real registration dead end, found only by reproducing it live: the
/// stated password rule ("At least 8 characters") was wrong — AddIdentityCore only overrides
/// RequiredLength and RequireNonAlphanumeric, leaving Identity's digit/upper/lowercase defaults in
/// force — and the client's ReadErrorAsync only understood the `{ "error": "..." }` shape, while
/// /api/auth/register actually returns ProblemDetails (`title`/`detail`). A user typing "password"
/// satisfied the client's own validation and then got "Request failed with status 400." with no
/// indication of what to fix.
/// </summary>
[Collection("Integration")]
public class RegisterErrorMessageTests(BookItWebApplicationFactory factory)
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Register_WithAPasswordMissingAnUppercaseLetter_ReturnsASpecificReason()
    {
        var email = $"user-{Guid.NewGuid():N}@test.local";

        var response = await client.PostAsJsonAsync("api/auth/register", new RegisterRequest(email, "password1", "Test User"));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // The old generic "Could not create an account with the provided details." must NOT be
        // what a password-policy failure returns — that message is now reserved for the
        // enumeration-sensitive duplicate-email case.
        Assert.DoesNotContain("Could not create an account with the provided details.", body);
        Assert.Contains("uppercase", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_WithADuplicateEmail_StillReturnsTheGenericEnumerationSafeMessage()
    {
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var firstResponse = await client.PostAsJsonAsync("api/auth/register", new RegisterRequest(email, "Passw0rd123", "First User"));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await client.PostAsJsonAsync("api/auth/register", new RegisterRequest(email, "Passw0rd123", "Second User"));

        var body = await secondResponse.Content.ReadAsStringAsync();
        Assert.Contains("Could not create an account with the provided details.", body);
    }
}
