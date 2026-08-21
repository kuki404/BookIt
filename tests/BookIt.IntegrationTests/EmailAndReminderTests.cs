using System.Data.Common;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookIt.Application.Dtos;
using BookIt.Domain.Enums;
using BookIt.Infrastructure;
using BookIt.Infrastructure.Email;
using BookIt.Infrastructure.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Respawn;
using Shouldly;
using Testcontainers.MsSql;

namespace BookIt.IntegrationTests;

/// <summary>
/// A separate factory/container from <see cref="BookItWebApplicationFactory"/> (not part of the
/// shared "Integration" collection) because it swaps two real dependencies for test doubles that
/// only this test class should see: <see cref="IEmailSender"/> is replaced with an NSubstitute
/// mock so email-sending can be asserted directly instead of inferred from SMTP log warnings, and
/// <see cref="TimeProvider"/> is replaced with a <see cref="FakeTimeProvider"/> so
/// BookingReminderService's reminder-window sweep (which runs continuously via a hosted
/// BackgroundService for the lifetime of the host) can be driven deterministically by advancing
/// the fake clock instead of waiting on a real 5-minute timer. Everything else — the DbContext,
/// the real overlap-check transaction logic, the real HTTP pipeline — stays real; only the SMTP
/// client and the wall clock are doubled.
/// </summary>
public class EmailTestWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string SaPassword = "IntegrationTests123!";

    private readonly MsSqlContainer sqlContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword(SaPassword)
        .Build();

    private DbConnection connection = null!;
    private Respawner respawner = null!;

    public IEmailSender EmailSender { get; } = Substitute.For<IEmailSender>();

    // Anchored to real wall-clock time, not an arbitrary fixed date: JwtBearer's token-lifetime
    // validation checks the "exp" claim (computed from this TimeProvider) against the real system
    // clock, not against this fake one — an arbitrary past/future anchor would make every issued
    // access token look already-expired or not-yet-valid to that real-clock check.
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public async ValueTask InitializeAsync()
    {
        await sqlContainer.StartAsync();

        Environment.SetEnvironmentVariable("Sql__Host", sqlContainer.Hostname);
        Environment.SetEnvironmentVariable("Sql__Port", sqlContainer.GetMappedPublicPort(1433).ToString());
        Environment.SetEnvironmentVariable("Sql__Password", SaPassword);
        Environment.SetEnvironmentVariable("Sql__Database", "BookIt_EmailReminderTests");
        Environment.SetEnvironmentVariable("Jwt__Secret", "integration-tests-signing-key-at-least-32-characters-long");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "BookIt.Api");
        Environment.SetEnvironmentVariable("Jwt__Audience", "BookIt.Client");
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", "http://localhost:5232");
        Environment.SetEnvironmentVariable("RateLimiting__Auth__PermitLimit", "1000");

        using (var warmUpClient = CreateClient())
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BookItDbContext>();
            await db.Database.MigrateAsync();
        }

        var sqlConnection = new SqlConnection(BuildAdoConnectionString());
        await sqlConnection.OpenAsync();

        respawner = await Respawner.CreateAsync(sqlConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            SchemasToInclude = ["dbo"],
            TablesToIgnore = ["__EFMigrationsHistory"]
        });

        connection = sqlConnection;
    }

    public async Task ResetDatabaseAsync()
    {
        await respawner.ResetAsync(connection);

        using var scope = Services.CreateScope();
        await DbInitializer.RunAsync(scope.ServiceProvider);

        EmailSender.ClearReceivedCalls();
    }

    private string BuildAdoConnectionString() =>
        $"Server={sqlContainer.Hostname},{sqlContainer.GetMappedPublicPort(1433)};" +
        "Database=BookIt_EmailReminderTests;User Id=sa;Password=" + SaPassword +
        ";TrustServerCertificate=True;Encrypt=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton(EmailSender);

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();
        await sqlContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("EmailAndReminder")]
public class EmailAndReminderCollection : ICollectionFixture<EmailTestWebApplicationFactory>;

[Collection("EmailAndReminder")]
public class EmailAndReminderTests(EmailTestWebApplicationFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateBooking_SendsConfirmationEmail_ToTheBookingOwner()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var (token, email) = await RegisterAndGetTokenAsync(client);
        var resourceId = await CreateDedicatedResourceAsync(client);
        var start = factory.Clock.GetUtcNow().UtcDateTime.AddDays(1);

        var response = await PostBookingAsync(client, token, resourceId, start, start.AddHours(1));
        response.EnsureSuccessStatusCode();
        var booking = await response.Content.ReadFromJsonAsync<BookingDto>(Ct);

        await factory.EmailSender.Received(1).SendAsync(
            email,
            Arg.Is<string>(subject => subject.Contains("Booking received")),
            Arg.Is<string>(body => body.Contains(booking!.ReferenceCode)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelBooking_SendsCancellationEmail_DistinctFromTheConfirmationEmail()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var (token, email) = await RegisterAndGetTokenAsync(client);
        var resourceId = await CreateDedicatedResourceAsync(client);
        var start = factory.Clock.GetUtcNow().UtcDateTime.AddDays(2);

        var createResponse = await PostBookingAsync(client, token, resourceId, start, start.AddHours(1));
        createResponse.EnsureSuccessStatusCode();
        var booking = await createResponse.Content.ReadFromJsonAsync<BookingDto>(Ct);
        factory.EmailSender.ClearReceivedCalls();

        var cancelRequest = new HttpRequestMessage(HttpMethod.Post, $"api/bookings/{booking!.Id}/cancel")
        {
            Content = JsonContent.Create(new CancelBookingRequest("no longer needed"))
        };
        cancelRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var cancelResponse = await client.SendAsync(cancelRequest, Ct);
        cancelResponse.EnsureSuccessStatusCode();

        // Exactly one email this time (the confirmation call was cleared above), and it is the
        // distinct cancellation email, not a repeat of the booking-received one.
        await factory.EmailSender.Received(1).SendAsync(
            email,
            Arg.Is<string>(subject => subject.Contains("Booking cancelled")),
            Arg.Is<string>(body => body.Contains(booking.ReferenceCode)),
            Arg.Any<CancellationToken>());
        await factory.EmailSender.DidNotReceive().SendAsync(
            email,
            Arg.Is<string>(subject => subject.Contains("Booking received")),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Proves the TimeProvider adoption in BookingReminderService actually works, not just that it
    /// compiles: creates + confirms a booking whose start sits inside the 2-hour reminder window,
    /// advances the FakeTimeProvider clock past the service's 5-minute sweep interval to drive a
    /// real tick of its PeriodicTimer(TimeSpan, TimeProvider), and asserts the reminder email goes
    /// out and the booking is marked as reminded — the whole point of adopting the abstraction.
    /// </summary>
    [Fact]
    public async Task AdvancingTheFakeClock_TriggersReminderSweep_ForABookingInsideTheReminderWindow()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        var (token, email) = await RegisterAndGetTokenAsync(client);
        var resourceId = await CreateDedicatedResourceAsync(client);

        // Inside the 2-hour reminder window as of "now" on the fake clock.
        var start = factory.Clock.GetUtcNow().UtcDateTime.AddHours(1);
        var createResponse = await PostBookingAsync(client, token, resourceId, start, start.AddHours(1));
        createResponse.EnsureSuccessStatusCode();
        var booking = await createResponse.Content.ReadFromJsonAsync<BookingDto>(Ct);

        await ConfirmAsAdminAsync(client, booking!.Id);
        factory.EmailSender.ClearReceivedCalls();

        // Advance past BookingReminderService's 5-minute sweep interval — this releases the
        // PeriodicTimer's WaitForNextTickAsync and drives exactly one more sweep synchronously,
        // no wall-clock waiting involved.
        factory.Clock.Advance(BookIt.Infrastructure.BackgroundJobs.BookingReminderService.SweepInterval + TimeSpan.FromSeconds(1));

        await WaitUntilAsync(async () =>
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BookItDbContext>();
            var reminded = await db.Bookings.AsNoTracking().Where(b => b.Id == booking.Id).Select(b => b.ReminderSentAtUtc).FirstAsync(Ct);
            return reminded is not null;
        });

        await factory.EmailSender.Received(1).SendAsync(
            email,
            Arg.Is<string>(subject => subject.Contains("Reminder")),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100, Ct);
        }

        (await condition()).ShouldBeTrue("Timed out waiting for the reminder sweep to run.");
    }

    private static async Task ConfirmAsAdminAsync(HttpClient client, Guid bookingId)
    {
        var loginResponse = await client.PostAsJsonAsync("api/auth/login", new LoginRequest("admin@bookit.local", "Admin123!"), Ct);
        loginResponse.EnsureSuccessStatusCode();
        var adminAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>(Ct);

        var request = new HttpRequestMessage(HttpMethod.Post, $"api/bookings/{bookingId}/confirm");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth!.AccessToken);
        var response = await client.SendAsync(request, Ct);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<(string Token, string Email)> RegisterAndGetTokenAsync(HttpClient client)
    {
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var response = await client.PostAsJsonAsync("api/auth/register", new RegisterRequest(email, "Passw0rd!", "Test User"), Ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>(Ct);
        return (body!.AccessToken, email);
    }

    // A freshly created resource per test (its id returned straight from the POST response body)
    // rather than reading the seeded catalog back through GET api/resources: that read goes
    // through ResourceCache (HybridCache, 5-minute entries), which Respawn's table truncation in
    // ResetDatabaseAsync does not invalidate — a cached listing from an earlier test in this
    // process would still hold pre-reset resource ids that no longer exist in the fresh database.
    // Creating a resource invalidates the cache as a side effect and hands back a guaranteed-fresh id.
    private static async Task<Guid> CreateDedicatedResourceAsync(HttpClient client)
    {
        var loginResponse = await client.PostAsJsonAsync("api/auth/login", new LoginRequest("admin@bookit.local", "Admin123!"), Ct);
        loginResponse.EnsureSuccessStatusCode();
        var adminAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>(Ct);

        var request = new HttpRequestMessage(HttpMethod.Post, "api/resources")
        {
            Content = JsonContent.Create(new CreateResourceRequest($"Test Room {Guid.NewGuid():N}", ResourceType.Room, 4, null))
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
