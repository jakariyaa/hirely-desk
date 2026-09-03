using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Infrastructure.Data.Configurations;

public class ProfileAttributeValueConfiguration : IEntityTypeConfiguration<ProfileAttributeValue>
{
    public void Configure(EntityTypeBuilder<ProfileAttributeValue> builder)
    {
        builder.Property(v => v.Version).IsConcurrencyToken();

        builder.HasIndex(v => new { v.ProfileId, v.AttributeDefinitionId }).IsUnique();

        builder.HasOne(v => v.AttributeDefinition)
            .WithMany()
            .HasForeignKey(v => v.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
