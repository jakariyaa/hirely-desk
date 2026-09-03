using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Core.Data;

public interface IAppDbContext : IDisposable, IAsyncDisposable
{
    DbSet<ApplicationUser> Users { get; }
    DbSet<Profile> Profiles { get; }
    DbSet<AttributeCategory> AttributeCategories { get; }
    DbSet<AttributeDefinition> AttributeDefinitions { get; }
    DbSet<ProfileAttributeValue> ProfileAttributeValues { get; }
    DbSet<Project> Projects { get; }
    DbSet<ProjectTag> ProjectTags { get; }
    DbSet<ProjectTagLink> ProjectTagLinks { get; }
    DbSet<Position> Positions { get; }
    DbSet<PositionAttribute> PositionAttributes { get; }
    DbSet<AccessRule> AccessRules { get; }
    DbSet<Cv> Cvs { get; }
    DbSet<CvProject> CvProjects { get; }
    DbSet<DiscussionPost> DiscussionPosts { get; }
    DbSet<CvLike> CvLikes { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default);
}
