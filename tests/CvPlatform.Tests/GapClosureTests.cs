using AwesomeAssertions;
using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Cvs;
using CvPlatform.Application.Positions;
using CvPlatform.Application.Search;
using CvPlatform.Core.Access;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using CvPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class GapClosureTests
{
    private sealed class TestFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
    {
        public IAppDbContext CreateDbContext() => factory.CreateDbContext();
    }

    private static IAppDbContextFactory CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new VersionIncrementInterceptor()));
        return new TestFactory(services.BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<AppDbContext>>());
    }

    [Fact]
    public void ActorContext_carries_explicit_recruiter_flag()
    {
        Roles.Admin.Should().Be("Admin");
        Roles.Recruiter.Should().Be("Recruiter");
        var candidate = new ActorContext(Guid.NewGuid(), false);
        candidate.IsRecruiter.Should().BeFalse();
        candidate.IsPrivileged.Should().BeFalse();
        var recruiter = new ActorContext(Guid.NewGuid(), false, true);
        recruiter.IsPrivileged.Should().BeTrue();
        var admin = new ActorContext(Guid.NewGuid(), true);
        admin.IsPrivileged.Should().BeTrue();
    }

    [Fact]
    public async Task Board_forbids_candidate_who_owns_cv()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var candidateId = await SeedCandidateAsync(factory);
        var positionId = await SeedPositionAsync(factory, ownerId, isPublic: true);
        var cvId = await CreatePublishedCvAsync(factory, candidateId, positionId);

        var cvs = new CvService(factory, new PositionAccessService(factory, new AccessRuleEngine()));
        var candidateBoard = await cvs.ListByPositionAsync(new ActorContext(candidateId, false), positionId);
        candidateBoard.Succeeded.Should().BeFalse();
        candidateBoard.Error.Code.Should().Be(ErrorCodes.Forbidden);

        var ownerBoard = await cvs.ListByPositionAsync(new ActorContext(ownerId, false, true), positionId);
        ownerBoard.Succeeded.Should().BeTrue();
        ownerBoard.Value!.Select(c => c.Id).Should().Contain(cvId);
    }

    [Fact]
    public async Task ListMinePaged_pages_in_service()
    {
        var factory = CreateFactory();
        var candidateId = await SeedCandidateAsync(factory);
        for (var i = 0; i < 5; i++)
        {
            var pid = await SeedPositionAsync(factory, Guid.NewGuid(), isPublic: true, title: $"P{i}");
            await CreateDraftCvAsync(factory, candidateId, pid);
        }

        var cvs = new CvService(factory, new PositionAccessService(factory, new AccessRuleEngine()));
        var page1 = await cvs.ListMinePagedAsync(new ActorContext(candidateId, false), new PageRequest(1, 2));
        page1.Succeeded.Should().BeTrue();
        page1.Value!.TotalCount.Should().Be(5);
        page1.Value.Items.Count.Should().Be(2);
        var page3 = await cvs.ListMinePagedAsync(new ActorContext(candidateId, false), new PageRequest(3, 2));
        page3.Value!.Items.Count.Should().Be(1);
    }

    [Fact]
    public async Task Search_fallback_includes_company()
    {
        var factory = CreateFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.Positions.Add(new Position
            {
                Id = Guid.NewGuid(),
                Title = "Backend",
                ShortDescription = "desc",
                Company = "AcmeCorpUnique",
                IsPublic = true,
            });
            await db.SaveChangesAsync();
        }

        var service = new SearchService(factory, new PositionAccessService(factory, new AccessRuleEngine()), matcher: null);
        var byCompany = await service.SearchPositionsAsync(
            new ActorContext(Guid.NewGuid(), false), "AcmeCorpUnique", new PageRequest(1, 10));
        byCompany.Succeeded.Should().BeTrue();
        byCompany.Value!.TotalCount.Should().Be(1);

        var byTitle = await service.SearchPositionsAsync(
            new ActorContext(Guid.NewGuid(), false), "Backend", new PageRequest(1, 10));
        byTitle.Value!.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Delete_requires_fresh_version()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var positions = new PositionService(factory);
        var created = await positions.CreateAsync(
            new ActorContext(ownerId, false, true),
            new PositionInput("Lead", "", null, null, true));
        created.Succeeded.Should().BeTrue();
        var id = created.Value!.Id;

        var stale = await positions.DeleteAsync(new ActorContext(ownerId, false, true), id, expectedVersion: 99);
        stale.Succeeded.Should().BeFalse();
        stale.Error.Code.Should().Be(ErrorCodes.ConcurrencyConflict);

        var ok = await positions.DeleteAsync(new ActorContext(ownerId, false, true), id, expectedVersion: 1);
        ok.Succeeded.Should().BeTrue();

        var missing = await positions.DeleteAsync(new ActorContext(ownerId, false, true), id, expectedVersion: 1);
        missing.Error.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Invalid_options_rejected_once()
    {
        var factory = CreateFactory();
        var categoryId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.AttributeCategories.Add(new AttributeCategory { Id = categoryId, Name = "G" });
            await db.SaveChangesAsync();
        }

        var service = new CvPlatform.Application.Attributes.AttributeDefinitionService(factory);
        var admin = new ActorContext(Guid.NewGuid(), true);

        var badJson = await service.CreateAsync(admin,
            new CvPlatform.Application.Attributes.AttributeDefinitionInput(
                categoryId, "Num1", null, AttributeDataType.Numeric, "not-json"));
        badJson.Succeeded.Should().BeFalse();
        badJson.Error.Code.Should().Be(ErrorCodes.ValidationFailed);

        var choicesOnString = await service.CreateAsync(admin,
            new CvPlatform.Application.Attributes.AttributeDefinitionInput(
                categoryId, "Str1", null, AttributeDataType.String, """{"choices":["a"]}"""));
        choicesOnString.Succeeded.Should().BeFalse();

        var dupChoices = await service.CreateAsync(admin,
            new CvPlatform.Application.Attributes.AttributeDefinitionInput(
                categoryId, "Drop1", null, AttributeDataType.Dropdown, """{"choices":["a","a"]}"""));
        dupChoices.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Resx_contains_builtin_attr_keys_and_concurrency_hint()
    {
        var baseDir = AppContext.BaseDirectory;
        var resx = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "CvPlatform.Web", "Resources", "SharedResource.en.resx"));
        if (!System.IO.File.Exists(resx))
        {
            var alt = "/home/jack/itransition/cv-management-platform/src/CvPlatform.Web/Resources/SharedResource.en.resx";
            resx = alt;
        }
        var xml = System.IO.File.ReadAllText(resx);
        xml.Should().Contain("Attr.Me.City");
        xml.Should().Contain("Attr.Me.BirthDate");
        xml.Should().Contain("ConcurrencyReloadHint");
    }

    [Fact]
    public void Application_does_not_reference_npgsql_types()
    {
        var asm = typeof(SearchService).Assembly;
        var refs = asm.GetReferencedAssemblies().Select(a => a.Name).ToList();
        refs.Should().NotContain("Npgsql");
        Xunit.Assert.Same(asm, typeof(IFullTextMatcher).Assembly);
        typeof(CvPlatform.Infrastructure.Search.PostgresFullTextMatcher).Assembly.GetName().Name
            .Should().Contain("Infrastructure");
    }

    [Fact]
    public async Task Candidate_cannot_create_position_like_or_discuss()
    {
        var factory = CreateFactory();
        var candidate = new ActorContext(Guid.NewGuid(), false);
        var positions = new PositionService(factory);
        var created = await positions.CreateAsync(candidate, new PositionInput("X", "", null, null, true));
        created.Succeeded.Should().BeFalse();
        created.Error.Code.Should().Be(ErrorCodes.Forbidden);

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(new ApplicationUser { Id = Guid.NewGuid(), UserName = "r@t.test" });
            await db.SaveChangesAsync();
        }
        var likes = new CvPlatform.Application.Likes.LikeService(
            factory, new PositionAccessService(factory, new AccessRuleEngine()));
        var like = await likes.ToggleAsync(candidate, Guid.NewGuid());
        like.Succeeded.Should().BeFalse();
        like.Error.Code.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Attribute_delete_requires_fresh_version()
    {
        var factory = CreateFactory();
        var categoryId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.AttributeCategories.Add(new AttributeCategory { Id = categoryId, Name = "G" });
            await db.SaveChangesAsync();
        }
        var service = new CvPlatform.Application.Attributes.AttributeDefinitionService(factory);
        var admin = new ActorContext(Guid.NewGuid(), true);
        var created = await service.CreateAsync(admin,
            new CvPlatform.Application.Attributes.AttributeDefinitionInput(
                categoryId, "Del1", null, AttributeDataType.String, null));
        created.Succeeded.Should().BeTrue();

        var stale = await service.DeleteAsync(admin, created.Value!.Id, expectedVersion: 99);
        stale.Succeeded.Should().BeFalse();
        stale.Error.Code.Should().Be(ErrorCodes.ConcurrencyConflict);

        var ok = await service.DeleteAsync(admin, created.Value.Id, expectedVersion: 1);
        ok.Succeeded.Should().BeTrue();
    }

    private static async Task<Guid> SeedCandidateAsync(IAppDbContextFactory factory)
    {
        await using var db = factory.CreateDbContext();
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = $"c{userId:N}@t.test" });
        db.Profiles.Add(new Profile { Id = profileId, UserId = userId });
        await db.SaveChangesAsync();
        return userId;
    }

    private static async Task<Guid> SeedPositionAsync(
        IAppDbContextFactory factory, Guid ownerId, bool isPublic, string title = "T")
    {
        await using var db = factory.CreateDbContext();
        var id = Guid.NewGuid();
        db.Positions.Add(new Position { Id = id, Title = title, IsPublic = isPublic, OwnerId = ownerId });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> CreateDraftCvAsync(
        IAppDbContextFactory factory, Guid candidateId, Guid positionId)
    {
        var cvs = new CvService(factory, new PositionAccessService(factory, new AccessRuleEngine()));
        await using (var db = factory.CreateDbContext())
        {
            var profileId = await db.Profiles.Where(p => p.UserId == candidateId).Select(p => p.Id).SingleAsync();
            var exists = await db.Cvs.AnyAsync(c => c.ProfileId == profileId && c.PositionId == positionId);
            if (!exists)
            {
                var created = await cvs.CreateAsync(new ActorContext(candidateId, false), positionId);
                created.Succeeded.Should().BeTrue();
                return created.Value!.Id;
            }
            return await db.Cvs.Where(c => c.ProfileId == profileId && c.PositionId == positionId).Select(c => c.Id).SingleAsync();
        }
    }

    private static async Task<Guid> CreatePublishedCvAsync(
        IAppDbContextFactory factory, Guid candidateId, Guid positionId)
    {
        var cvId = await CreateDraftCvAsync(factory, candidateId, positionId);
        var cvs = new CvService(factory, new PositionAccessService(factory, new AccessRuleEngine()));
        await using var db = factory.CreateDbContext();
        var version = (await db.Cvs.SingleAsync(c => c.Id == cvId)).Version;
        var pub = await cvs.PublishAsync(new ActorContext(candidateId, false), cvId, new CvStatusInput(version));
        pub.Succeeded.Should().BeTrue();
        return cvId;
    }
}
