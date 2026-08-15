using BookIt.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookIt.Infrastructure.Configurations;

public class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        builder.ToTable("Resources");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(20);

        // Backs the catalog listing query (ResourceService: filter IsActive, order by Name) —
        // covers both the WHERE and the ORDER BY in one index instead of a separate sort step.
        builder.HasIndex(r => new { r.IsActive, r.Name });
    }
}
