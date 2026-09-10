using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Infrastructure.Data.Configurations;

public sealed class PositionTemplateAccessRuleConfiguration : IEntityTypeConfiguration<PositionTemplateAccessRule>
{
    public void Configure(EntityTypeBuilder<PositionTemplateAccessRule> builder)
    {
        builder.Property(r => r.DataType).HasConversion<string>().IsRequired();
        builder.Property(r => r.Operator).HasConversion<string>().IsRequired();
        builder.Property(r => r.ComparisonValue).HasMaxLength(500).IsRequired();
        builder.HasIndex(r => new { r.PositionTemplateId, r.AttributeDefinitionId });
        builder.HasOne(r => r.PositionTemplate)
            .WithMany(t => t.AccessRules)
            .HasForeignKey(r => r.PositionTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.AttributeDefinition)
            .WithMany()
            .HasForeignKey(r => r.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
