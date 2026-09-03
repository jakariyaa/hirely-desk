using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Infrastructure.Data.Configurations;

public class CvConfiguration : IEntityTypeConfiguration<Cv>
{
    public void Configure(EntityTypeBuilder<Cv> builder)
    {
        builder.Property(c => c.Status).HasConversion<string>().IsRequired();
        builder.Property(c => c.Version).IsConcurrencyToken();

        builder.Property(c => c.SearchVector)
            .IsGeneratedTsVectorColumn("english", nameof(Cv.SearchText));
        builder.HasIndex(c => c.SearchVector).HasMethod("gin");

        builder.HasIndex(c => new { c.ProfileId, c.PositionId }).IsUnique();

        builder.HasOne(c => c.Profile)
            .WithMany()
            .HasForeignKey(c => c.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Position)
            .WithMany()
            .HasForeignKey(c => c.PositionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.IncludedProjects)
            .WithOne(p => p.Cv)
            .HasForeignKey(p => p.CvId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
