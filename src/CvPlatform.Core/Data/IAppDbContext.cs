using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CvPlatform.Core.Data;

public interface IAppDbContext : IDisposable, IAsyncDisposable
{
    DatabaseFacade Database { get; }
    EntityEntry<T> Entry<T>(T entity) where T : class;
    DbSet<ApplicationUser> Users { get; }
    DbSet<Profile> Profiles { get; }
    DbSet<AttributeCategory> AttributeCategories { get; }
    DbSet<AttributeDefinition> AttributeDefinitions { get; }
    DbSet<ProfileAttributeValue> ProfileAttributeValues { get; }
    DbSet<Project> Projects { get; }
    DbSet<ProjectTag> ProjectTags { get; }
    DbSet<ProjectTagLink> ProjectTagLinks { get; }
    DbSet<Position> Positions { get; }
    DbSet<PositionTemplate> PositionTemplates { get; }
    DbSet<PositionTemplateAttribute> PositionTemplateAttributes { get; }
    DbSet<PositionTemplateAccessRule> PositionTemplateAccessRules { get; }
    DbSet<PositionAttribute> PositionAttributes { get; }
    DbSet<AccessRule> AccessRules { get; }
    DbSet<Cv> Cvs { get; }
    DbSet<CvProject> CvProjects { get; }
    DbSet<DiscussionPost> DiscussionPosts { get; }
    DbSet<CvLike> CvLikes { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default);
}
