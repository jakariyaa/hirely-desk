using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Infrastructure.Data.Configurations;

public class AccessRuleConfiguration : IEntityTypeConfiguration<AccessRule>
{
    public void Configure(EntityTypeBuilder<AccessRule> builder)
    {
        builder.Property(r => r.DataType).HasConversion<string>().IsRequired();
        builder.Property(r => r.Operator).HasConversion<string>().IsRequired();
        builder.Property(r => r.ComparisonValue).IsRequired();

        builder.HasOne(r => r.AttributeDefinition)
            .WithMany()
            .HasForeignKey(r => r.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
