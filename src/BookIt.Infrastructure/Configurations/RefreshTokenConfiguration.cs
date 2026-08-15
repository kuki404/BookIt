using BookIt.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookIt.Infrastructure.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).HasMaxLength(256).IsRequired();
        // Backs the refresh/revoke lookup (find "this exact token") — the hottest query on this table.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Backs reuse-detection's "revoke every other active token for this user" bulk update in
        // AuthController.Refresh — filtered so the index stays small as revoked history accumulates.
        builder.HasIndex(t => new { t.UserId, t.RevokedAtUtc })
            .HasFilter("[RevokedAtUtc] IS NULL");
    }
}
