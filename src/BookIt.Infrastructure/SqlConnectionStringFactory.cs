using Microsoft.Extensions.Configuration;

namespace BookIt.Infrastructure;

/// <summary>
/// Builds the SQL Server connection string from configuration instead of a hardcoded string, so
/// the same code path works for local dev (User Secrets), CI (environment variables) and the
/// containerized app (docker-compose environment) without ever committing a password to git.
/// </summary>
public static class SqlConnectionStringFactory
{
    public static string Build(IConfiguration configuration)
    {
        var host = configuration["Sql:Host"] ?? "localhost";
        var port = configuration["Sql:Port"] ?? "1433";
        var database = configuration["Sql:Database"] ?? "BookIt";
        var password = configuration["Sql:Password"]
            ?? throw new InvalidOperationException(
                "Sql:Password is not configured. Set it with " +
                "'dotnet user-secrets set \"Sql:Password\" \"<password>\" --project src/BookIt.Api' " +
                "(must match MSSQL_SA_PASSWORD in your .env file).");

        return $"Server={host},{port};Database={database};User Id=sa;Password={password};TrustServerCertificate=True";
    }
}
