using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Infrastructure.Data.Configurations;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.Property(p => p.Name).IsRequired();
        builder.Property(p => p.Version).IsConcurrencyToken();

        builder.HasMany(p => p.Tags)
            .WithMany(t => t.Projects)
            .UsingEntity<ProjectTagLink>(
                l => l.HasOne<ProjectTag>().WithMany().HasForeignKey(l => l.ProjectTagId).OnDelete(DeleteBehavior.Cascade),
                l => l.HasOne<Project>().WithMany().HasForeignKey(l => l.ProjectId).OnDelete(DeleteBehavior.Cascade),
                l => l.HasKey(x => new { x.ProjectId, x.ProjectTagId }));
    }
}
