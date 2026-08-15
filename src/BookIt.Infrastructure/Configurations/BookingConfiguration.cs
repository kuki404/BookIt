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
        builder.HasIndex(b => b.UserId);
        builder.HasIndex(b => new { b.ResourceId, b.StartUtc, b.EndUtc });
    }
}
