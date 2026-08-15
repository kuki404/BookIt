using BookIt.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookIt.Infrastructure.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.ReferenceCode).HasMaxLength(16).IsRequired();
        // Backs GetByReferenceCodeAsync — customer-facing "look up my booking by code" lookups.
        builder.HasIndex(b => b.ReferenceCode).IsUnique();

        builder.Property(b => b.Notes).HasMaxLength(1000);
        builder.Property(b => b.CancellationReason).HasMaxLength(500);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);

        // Optimistic concurrency: two concurrent PATCH requests (e.g. confirm + cancel racing)
        // on the same row will have the second one fail with DbUpdateConcurrencyException.
        builder.Property(b => b.RowVersion).IsRowVersion();

        builder.HasOne(b => b.Resource)
            .WithMany(r => r.Bookings)
            .HasForeignKey(b => b.ResourceId)
            .OnDelete(DeleteBehavior.Restrict);

        // UserId points at ApplicationUser.Id (AspNetUsers) without a navigation property here,
        // to keep Booking's EF configuration decoupled from the Identity-specific DbContext setup.
        // Backs "my bookings" (BookingService.GetForUserAsync).
        builder.HasIndex(b => b.UserId);

        // Backs both the overlap-check compiled query (BookingService.CompiledHasOverlapQuery)
        // and the availability endpoint — both filter by ResourceId + a time range. Filtered to
        // exclude cancelled bookings so the index only ever covers rows that can actually block a
        // new booking, keeping it smaller and cheaper to maintain on every write.
        builder.HasIndex(b => new { b.ResourceId, b.StartUtc, b.EndUtc })
            .HasFilter("[Status] <> 'Cancelled'");
    }
}
