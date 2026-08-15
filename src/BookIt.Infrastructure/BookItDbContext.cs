using BookIt.Domain.Entities;
using BookIt.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Infrastructure;

public class BookItDbContext(DbContextOptions<BookItDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(BookItDbContext).Assembly);

        // Identity's default table names (AspNetUsers, AspNetRoles, ...) are fine as-is; no
        // renaming needed since this is a green-field schema, not a migration off legacy tables.
    }
}
