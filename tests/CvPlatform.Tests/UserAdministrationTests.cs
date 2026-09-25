using AwesomeAssertions;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Core.Entities;
using CvPlatform.Infrastructure.Data;
using CvPlatform.Web.Seed;
using CvPlatform.Web.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CvPlatform.Tests;

public class UserAdministrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly ServiceProvider _serviceProvider;

    public UserAdministrationTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _serviceProvider = new ServiceCollection().BuildServiceProvider();
    }

    public void Dispose()
    {
        _db.Dispose();
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    private UserManager<ApplicationUser> Users()
    {
        var store = new UserStore<ApplicationUser, IdentityRole<Guid>, AppDbContext, Guid>(_db);
        return new UserManager<ApplicationUser>(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            _serviceProvider,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static TestFactory Factory(AppDbContext db) => new(db);

    private sealed class TestFactory(AppDbContext db) : CvPlatform.Core.Data.IAppDbContextFactory
    {
        public CvPlatform.Core.Data.IAppDbContext CreateDbContext() => new TestWrapper(db);
    }

    private sealed class TestWrapper(AppDbContext db) : CvPlatform.Core.Data.IAppDbContext
    {
        public DatabaseFacade Database => db.Database;
        public EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class => db.Entry(entity);
        public DbSet<ApplicationUser> Users => db.Users;
        public DbSet<Profile> Profiles => db.Profiles;
        public DbSet<AttributeCategory> AttributeCategories => db.AttributeCategories;
        public DbSet<AttributeDefinition> AttributeDefinitions => db.AttributeDefinitions;
        public DbSet<ProfileAttributeValue> ProfileAttributeValues => db.ProfileAttributeValues;
        public DbSet<Project> Projects => db.Projects;
        public DbSet<ProjectTag> ProjectTags => db.ProjectTags;
        public DbSet<ProjectTagLink> ProjectTagLinks => db.ProjectTagLinks;
        public DbSet<Position> Positions => db.Positions;
        public DbSet<PositionTemplate> PositionTemplates => db.PositionTemplates;
        public DbSet<PositionTemplateAttribute> PositionTemplateAttributes => db.PositionTemplateAttributes;
        public DbSet<PositionTemplateAccessRule> PositionTemplateAccessRules => db.PositionTemplateAccessRules;
        public DbSet<PositionAttribute> PositionAttributes => db.PositionAttributes;
        public DbSet<AccessRule> AccessRules => db.AccessRules;
        public DbSet<Cv> Cvs => db.Cvs;
        public DbSet<CvProject> CvProjects => db.CvProjects;
        public DbSet<DiscussionPost> DiscussionPosts => db.DiscussionPosts;
        public DbSet<CvLike> CvLikes => db.CvLikes;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
        public Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default) =>
            db.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Non_admin_cannot_delete_users()
    {
        var service = new UserAdministrationService(Factory(_db), Users());
        var result = await service.DeleteUserAsync(new ActorContext(Guid.NewGuid(), false), Guid.NewGuid());
        result.Error.Code.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Admin_account_cannot_be_deleted()
    {
        var users = Users();
        var admin = new ApplicationUser { UserName = SeedData.AdminEmail, Email = SeedData.AdminEmail };
        (await users.CreateAsync(admin, "Password123!")).Succeeded.Should().BeTrue();
        _db.Roles.Add(new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = SeedData.AdminRole, NormalizedName = SeedData.AdminRole.ToUpperInvariant() });
        await _db.SaveChangesAsync();
        var role = await _db.Roles.SingleAsync(r => r.Name == SeedData.AdminRole);
        _db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = admin.Id, RoleId = role.Id });
        await _db.SaveChangesAsync();

        var service = new UserAdministrationService(Factory(_db), Users());
        var actor = new ActorContext(admin.Id, true);
        var result = await service.DeleteUserAsync(actor, admin.Id);
        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.Forbidden);
    }
}
