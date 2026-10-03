using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Infrastructure.Data.Configurations;

public class PositionApiTokenConfiguration : IEntityTypeConfiguration<PositionApiToken>
{
    public void Configure(EntityTypeBuilder<PositionApiToken> builder)
    {
        builder.Property(t => t.Name).IsRequired().HasMaxLength(PositionApiToken.NameMaxLength);
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(t => t.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.PositionId);

        builder.HasOne(t => t.Position)
            .WithMany()
            .HasForeignKey(t => t.PositionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.CreatedBy)
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
