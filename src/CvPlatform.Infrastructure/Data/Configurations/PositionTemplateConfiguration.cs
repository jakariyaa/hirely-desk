using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Infrastructure.Data.Configurations;

public sealed class PositionTemplateConfiguration : IEntityTypeConfiguration<PositionTemplate>
{
    public void Configure(EntityTypeBuilder<PositionTemplate> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(2000).IsRequired();
        builder.Property(t => t.Company).HasMaxLength(200);
        builder.Property(t => t.Level).HasMaxLength(100);
        builder.Property(t => t.Version).IsConcurrencyToken();
        builder.Property(t => t.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").IsRequired();
        builder.Property(t => t.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").IsRequired();
        builder.HasIndex(t => new { t.IsActive, t.Name });

        builder.HasOne(t => t.CreatedBy)
            .WithMany()
            .HasForeignKey(t => t.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
