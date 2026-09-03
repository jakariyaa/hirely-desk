using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Infrastructure.Data.Configurations;

public class PositionAttributeConfiguration : IEntityTypeConfiguration<PositionAttribute>
{
    public void Configure(EntityTypeBuilder<PositionAttribute> builder)
    {
        builder.HasKey(a => new { a.PositionId, a.AttributeDefinitionId });

        builder.HasOne(a => a.AttributeDefinition)
            .WithMany()
            .HasForeignKey(a => a.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
