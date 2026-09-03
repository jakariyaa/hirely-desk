using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Infrastructure.Data.Configurations;

public class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.Property(p => p.Title).IsRequired();
        builder.Property(p => p.Version).IsConcurrencyToken();

        builder.Property(p => p.SearchVector)
            .IsGeneratedTsVectorColumn("english", nameof(Position.Title), nameof(Position.ShortDescription));
        builder.HasIndex(p => p.SearchVector).HasMethod("gin");

        builder.HasMany(p => p.Attributes)
            .WithOne(a => a.Position)
            .HasForeignKey(a => a.PositionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.AccessRules)
            .WithOne(r => r.Position)
            .HasForeignKey(r => r.PositionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
