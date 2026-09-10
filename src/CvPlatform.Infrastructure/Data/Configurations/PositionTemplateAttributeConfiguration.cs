using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Infrastructure.Data.Configurations;

public sealed class PositionTemplateAttributeConfiguration : IEntityTypeConfiguration<PositionTemplateAttribute>
{
    public void Configure(EntityTypeBuilder<PositionTemplateAttribute> builder)
    {
        builder.HasKey(a => new { a.PositionTemplateId, a.AttributeDefinitionId });
        builder.HasOne(a => a.PositionTemplate)
            .WithMany(t => t.Attributes)
            .HasForeignKey(a => a.PositionTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(a => a.AttributeDefinition)
            .WithMany()
            .HasForeignKey(a => a.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
