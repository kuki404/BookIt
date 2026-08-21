using System.Data.Common;
using BookIt.Infrastructure;
using BookIt.Infrastructure.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Respawn;
using Testcontainers.MsSql;

namespace BookIt.IntegrationTests;

/// <summary>
/// One real SQL Server container for the whole test run (Testcontainers.MsSql), Respawn to reset
/// state between tests instead of a container per test — per dotnet-testing. No more manually
/// started `docker compose up -d db` or dev-machine user-secrets: Testcontainers pulls up its own
/// disposable instance and every setting the app needs is set on the process environment before
/// the host is built.
///
/// The override is set as a process environment variable, not via WebApplicationFactory's
/// ConfigureWebHost/ConfigureAppConfiguration hook: Program.cs (top-level statements) reads
/// configuration to build the connection string before WebApplicationFactory's host-building
/// customization is applied, so that hook arrives too late here. Environment variables are picked
/// up because they're added as a configuration source at the very start of
/// WebApplicationBuilder.CreateBuilder(), before any app code runs — the same technique
/// StaySphere uses, adapted for Testcontainers' dynamically assigned port.
/// </summary>
public class BookItWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string SaPassword = "IntegrationTests123!";

    private readonly MsSqlContainer sqlContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword(SaPassword)
        .Build();

    private DbConnection connection = null!;
    private Respawner respawner = null!;

    public async ValueTask InitializeAsync()
    {
        await sqlContainer.StartAsync();

        Environment.SetEnvironmentVariable("Sql__Host", sqlContainer.Hostname);
        Environment.SetEnvironmentVariable("Sql__Port", sqlContainer.GetMappedPublicPort(1433).ToString());
        Environment.SetEnvironmentVariable("Sql__Password", SaPassword);
        Environment.SetEnvironmentVariable("Sql__Database", "BookIt_IntegrationTests");
        Environment.SetEnvironmentVariable("Jwt__Secret", "integration-tests-signing-key-at-least-32-characters-long");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "BookIt.Api");
        Environment.SetEnvironmentVariable("Jwt__Audience", "BookIt.Client");
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", "http://localhost:5232");

        // The "auth" rate limit (5/min) exists to slow down brute-force attempts, not to survive
        // a test suite that legitimately registers/logs in many times in a few seconds — except
        // for SecurityTests, which deliberately drives it to the lockout threshold itself.
        Environment.SetEnvironmentVariable("RateLimiting__Auth__PermitLimit", "1000");

        // Triggers host build (lazy on first Server/CreateClient access). DbInitializer.RunAsync
        // (migration + seed) already runs unconditionally from Program.cs on startup, so no
        // separate migration/seed step is needed here.
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

    /// <summary>
    /// Respawn wipes every table (including the seeded admin/customer users and demo resources),
    /// so DbInitializer's seed is re-applied immediately after — it's idempotent (existence checks
    /// before every insert), and several tests (e.g. CreateDedicatedResourceAsync logging in as
    /// admin@bookit.local, GetResources_Anonymous_ReturnsSeededResources) depend on that seed data
    /// being present at the start of every test, not just the very first one.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        await respawner.ResetAsync(connection);

        using var scope = Services.CreateScope();
        await DbInitializer.RunAsync(scope.ServiceProvider);
    }

    private string BuildAdoConnectionString() =>
        $"Server={sqlContainer.Hostname},{sqlContainer.GetMappedPublicPort(1433)};" +
        "Database=BookIt_IntegrationTests;User Id=sa;Password=" + SaPassword +
        ";TrustServerCertificate=True;Encrypt=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Development");

    public override async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();
        await sqlContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}

/// <summary>
/// All integration test classes share ONE factory instance via this collection instead of each
/// declaring their own IClassFixture — xUnit runs test classes in different collections in
/// parallel, and two hosts migrating/seeding the same shared database at the same time race each
/// other ("database already exists", duplicate-key on the seeded demo user).
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<BookItWebApplicationFactory>;
