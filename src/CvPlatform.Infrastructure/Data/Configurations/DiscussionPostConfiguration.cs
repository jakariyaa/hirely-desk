using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Infrastructure.Data.Configurations;

public class DiscussionPostConfiguration : IEntityTypeConfiguration<DiscussionPost>
{
    public void Configure(EntityTypeBuilder<DiscussionPost> builder)
    {
        builder.Property(p => p.TextMarkdown).IsRequired();

        builder.HasIndex(p => new { p.PositionId, p.CreatedAt });

        builder.HasOne(p => p.Position)
            .WithMany()
            .HasForeignKey(p => p.PositionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Author)
            .WithMany()
            .HasForeignKey(p => p.AuthorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
