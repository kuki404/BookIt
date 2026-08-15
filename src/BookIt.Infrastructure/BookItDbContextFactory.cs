using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace BookIt.Infrastructure;

/// <summary>
/// Lets `dotnet ef migrations add` / `dotnet ef database update` build a DbContext without
/// spinning up the full Api host. Reads the same User Secrets store as BookIt.Api (shared
/// UserSecretsId), so no connection string is ever hardcoded or committed.
/// </summary>
public class BookItDbContextFactory : IDesignTimeDbContextFactory<BookItDbContext>
{
    public BookItDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<BookItDbContextFactory>()
            .AddEnvironmentVariables()
            .Build();

        var connectionString = SqlConnectionStringFactory.Build(configuration);

        var optionsBuilder = new DbContextOptionsBuilder<BookItDbContext>();
        optionsBuilder.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());

        return new BookItDbContext(optionsBuilder.Options);
    }
}
