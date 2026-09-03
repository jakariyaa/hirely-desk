using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Infrastructure.Data.Configurations;

public class ProjectTagLinkConfiguration : IEntityTypeConfiguration<ProjectTagLink>
{
    public void Configure(EntityTypeBuilder<ProjectTagLink> builder)
    {
        builder.HasKey(l => new { l.ProjectId, l.ProjectTagId });
    }
}
