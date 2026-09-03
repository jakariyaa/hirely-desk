using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Infrastructure.Data.Configurations;

public class CvLikeConfiguration : IEntityTypeConfiguration<CvLike>
{
    public void Configure(EntityTypeBuilder<CvLike> builder)
    {
        builder.HasKey(l => new { l.CvId, l.RecruiterId });

        builder.HasOne(l => l.Cv)
            .WithMany()
            .HasForeignKey(l => l.CvId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Recruiter)
            .WithMany()
            .HasForeignKey(l => l.RecruiterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
