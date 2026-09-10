using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<AttributeCategory> AttributeCategories => Set<AttributeCategory>();
    public DbSet<AttributeDefinition> AttributeDefinitions => Set<AttributeDefinition>();
    public DbSet<ProfileAttributeValue> ProfileAttributeValues => Set<ProfileAttributeValue>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectTag> ProjectTags => Set<ProjectTag>();
    public DbSet<ProjectTagLink> ProjectTagLinks => Set<ProjectTagLink>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<PositionTemplate> PositionTemplates => Set<PositionTemplate>();
    public DbSet<PositionTemplateAttribute> PositionTemplateAttributes => Set<PositionTemplateAttribute>();
    public DbSet<PositionTemplateAccessRule> PositionTemplateAccessRules => Set<PositionTemplateAccessRule>();
    public DbSet<PositionAttribute> PositionAttributes => Set<PositionAttribute>();
    public DbSet<AccessRule> AccessRules => Set<AccessRule>();
    public DbSet<Cv> Cvs => Set<Cv>();
    public DbSet<CvProject> CvProjects => Set<CvProject>();
    public DbSet<DiscussionPost> DiscussionPosts => Set<DiscussionPost>();
    public DbSet<CvLike> CvLikes => Set<CvLike>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        var providerName = Database.ProviderName;
        if (providerName is "Microsoft.EntityFrameworkCore.InMemory" or "Microsoft.EntityFrameworkCore.Sqlite")
        {
            modelBuilder.Entity<Cv>().Ignore("SearchVector");
            modelBuilder.Entity<Position>().Ignore("SearchVector");
        }
    }
}
