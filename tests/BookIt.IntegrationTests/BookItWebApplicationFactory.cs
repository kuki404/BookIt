using Microsoft.AspNetCore.Mvc.Testing;

namespace BookIt.IntegrationTests;

/// <summary>
/// Boots the real Api host (same DI wiring as production) against a dedicated
/// "BookIt_IntegrationTests" database on the SQL Server instance already running via
/// `docker compose up -d` — separate from the "BookIt" database used for local development, so
/// test runs never touch dev data. Reads the same User Secrets / environment variables as
/// BookIt.Api for the SQL password and JWT secret (see README for local setup).
///
/// The override is set as a process environment variable in the static constructor, not via
/// WebApplicationFactory's ConfigureWebHost/ConfigureAppConfiguration hook: Program.cs (top-level
/// statements) reads configuration to build the connection string before WebApplicationFactory's
/// host-building customization is applied, so that hook arrives too late here. Environment
/// variables are picked up because they're added as a configuration source at the very start of
/// WebApplicationBuilder.CreateBuilder(), before any app code runs.
/// </summary>
public class BookItWebApplicationFactory : WebApplicationFactory<Program>
{
    static BookItWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("Sql__Database", "BookIt_IntegrationTests");

        // The "auth" rate limit (5/min) exists to slow down brute-force attempts, not to survive
        // a test suite that legitimately registers/logs in many times in a few seconds.
        Environment.SetEnvironmentVariable("RateLimiting__Auth__PermitLimit", "1000");
    }
}
