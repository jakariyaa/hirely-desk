using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Infrastructure.Data.Configurations;

public class CvProjectConfiguration : IEntityTypeConfiguration<CvProject>
{
    public void Configure(EntityTypeBuilder<CvProject> builder)
    {
        builder.HasKey(p => new { p.CvId, p.ProjectId });

        builder.HasOne(p => p.Project)
            .WithMany()
            .HasForeignKey(p => p.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
