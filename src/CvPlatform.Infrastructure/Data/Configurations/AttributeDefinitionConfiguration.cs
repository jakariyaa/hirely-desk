using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Infrastructure.Data.Configurations;

public class AttributeDefinitionConfiguration : IEntityTypeConfiguration<AttributeDefinition>
{
    public void Configure(EntityTypeBuilder<AttributeDefinition> builder)
    {
        builder.Property(d => d.Name).IsRequired();
        builder.Property(d => d.DataType).HasConversion<string>().IsRequired();
        builder.Property(d => d.Version).IsConcurrencyToken();

        builder.HasIndex(d => d.Name).IsUnique();
    }
}
