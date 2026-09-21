using AwesomeAssertions;
using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Cvs;
using CvPlatform.Core.Access;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using CvPlatform.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

/// <summary>
/// Runs against Sqlite in-memory: proves the tsvector bypass and unique-index model
/// materialize on a relational provider (§7.5 Step 3 of the Phase 4 plan).
/// </summary>
public class CvServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IAppDbContextFactory _factory;

    public CvServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options
            .UseSqlite(_connection)
            .AddInterceptors(new VersionIncrementInterceptor()));
        var provider = services.BuildServiceProvider();
        var relationalFactory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using (var db = relationalFactory.CreateDbContext())
        {
            db.Database.EnsureCreated();
        }

        _factory = new TestFactory(relationalFactory);
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TestFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
    {
        public IAppDbContext CreateDbContext() => factory.CreateDbContext();
    }

    private static CvService Service(IAppDbContextFactory factory) =>
        new(factory, new PositionAccessService(factory, new AccessRuleEngine()));

    [Fact]
    public async Task Create_gated_by_access_rules_and_blocks_duplicates()
    {
        var definitionId = Guid.NewGuid();
        await SeedDefinitionAsync(_factory, definitionId, "City", AttributeDataType.String);
        var positionId = await SeedPositionAsync(_factory, isPublic: false,
            attributes: [(definitionId, "City", AttributeDataType.String, true)],
            rules: [(definitionId, RuleOperator.Equals, "Warsaw")]);
        var candidateId = await SeedCandidateAsync(_factory); // no values yet

        var actor = new ActorContext(candidateId, false);

        // Restricted position, no matching value yet → denied.
        var missing = await Service(_factory).CreateAsync(actor, positionId);
        missing.Error.Code.Should().Be(ErrorCodes.Forbidden);

        // Candidate fills the required value → access granted, CV created as Draft.
        await SeedValueAsync(_factory, candidateId, definitionId, "Warsaw");

        var ok = await Service(_factory).CreateAsync(actor, positionId);
        ok.Succeeded.Should().BeTrue();
        ok.Value!.Status.Should().Be(CvStatus.Draft);
        ok.Value!.UserId.Should().Be(candidateId);

        // One CV per profile/position.
        var duplicate = await Service(_factory).CreateAsync(actor, positionId);
        duplicate.Error.Code.Should().Be(ErrorCodes.Conflict);
    }
    [Fact]
    public async Task Create_requires_a_profile()
    {
        var positionId = await SeedPositionAsync(_factory, isPublic: true);
        var userId = await SeedUserWithoutProfileAsync(_factory);

        var result = await Service(_factory).CreateAsync(new ActorContext(userId, false), positionId);

        result.Error.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task GetRendered_computes_rows_over_position_template()
    {
        var defId = Guid.NewGuid();
        await SeedDefinitionAsync(_factory, defId, "City", AttributeDataType.String);
        var noteId = await SeedDefinitionAsync(_factory, Guid.NewGuid(), "Notes", AttributeDataType.Text);
        var positionId = await SeedPositionAsync(_factory, isPublic: true, attributes:
        [
            (defId, "City", AttributeDataType.String, true),
            (noteId, "Notes", AttributeDataType.Text, false),
        ]);
        var candidateId = await SeedCandidateAsync(_factory, ("City", AttributeDataType.String, "Warsaw", defId));
        var cvId = await CreateCvAsync(candidateId, positionId);

        var result = await Service(_factory).GetRenderedAsync(new ActorContext(candidateId, false), cvId);

        result.Succeeded.Should().BeTrue();
        var detail = result.Value!;
        detail.Rows.Should().HaveCount(2);
        detail.Rows[0].Name.Should().Be("City");
        detail.Rows[0].IsFilled.Should().BeTrue();
        detail.Rows[1].Name.Should().Be("Notes");
        detail.Rows[1].IsFilled.Should().BeFalse();
        detail.PublishGateSatisfied.Should().BeTrue();
        detail.MissingRequired.Should().BeEmpty();
        detail.CanEdit.Should().BeTrue();
    }

    [Fact]
    public async Task Publish_blocks_until_required_values_are_filled_then_computes_search_text()
    {
        var defId = Guid.NewGuid();
        await SeedDefinitionAsync(_factory, defId, "City", AttributeDataType.String);
        var positionId = await SeedPositionAsync(_factory, isPublic: true,
            attributes: [(defId, "City", AttributeDataType.String, true)]);
        var candidateId = await SeedCandidateAsync(_factory); // no values yet
        var cvId = await CreateCvAsync(candidateId, positionId);
        var actor = new ActorContext(candidateId, false);

        var version = await GetVersionAsync(cvId);
        var blocked = await Service(_factory).PublishAsync(actor, cvId, new CvStatusInput(version));
        blocked.Error.Code.Should().Be(ErrorCodes.ValidationFailed);
        blocked.Error.Message.Should().Contain("City");

        // Fill the value and publish for real.
        await SeedValueAsync(_factory, candidateId, defId, "Warsaw");

        var version2 = await GetVersionAsync(cvId);
        var published = await Service(_factory).PublishAsync(actor, cvId, new CvStatusInput(version2));
        published.Succeeded.Should().BeTrue();
        published.Value!.Status.Should().Be(CvStatus.Published);
        published.Value!.PublishedAt.Should().NotBeNull();

        await using var verify = _factory.CreateDbContext();
        var stored = await verify.Cvs.SingleAsync(c => c.Id == cvId);
        stored.SearchText.Should().Contain("Warsaw").And.Contain("City");
    }

    [Fact]
    public async Task Publish_rejects_stale_version_and_unpublish_clears_search_state()
    {
        var positionId = await SeedPositionAsync(_factory, isPublic: true);
        var candidateId = await SeedCandidateAsync(_factory);
        var cvId = await CreateCvAsync(candidateId, positionId);
        var actor = new ActorContext(candidateId, false);

        var stale = await Service(_factory).PublishAsync(actor, cvId, new CvStatusInput(999));
        stale.Error.Code.Should().Be(ErrorCodes.ConcurrencyConflict);

        var version = await GetVersionAsync(cvId);
        var published = await Service(_factory).PublishAsync(actor, cvId, new CvStatusInput(version));
        published.Succeeded.Should().BeTrue();

        var version2 = await GetVersionAsync(cvId);
        var unpublished = await Service(_factory).UnpublishAsync(actor, cvId, new CvStatusInput(version2));
        unpublished.Succeeded.Should().BeTrue();
        unpublished.Value!.Status.Should().Be(CvStatus.Draft);
        unpublished.Value!.PublishedAt.Should().BeNull();

        await using var verify = _factory.CreateDbContext();
        (await verify.Cvs.SingleAsync(c => c.Id == cvId)).SearchText.Should().BeNull();
    }

    [Fact]
    public async Task ToggleProject_respects_max_projects_and_inclusion_state()
    {
        var positionId = await SeedPositionAsync(_factory, isPublic: true, maxProjects: 2);
        var candidateId = await SeedCandidateAsync(_factory);
        var projectIdA = await SeedProjectAsync(_factory, candidateId, "Alpha");
        var projectIdB = await SeedProjectAsync(_factory, candidateId, "Beta");
        var projectIdC = await SeedProjectAsync(_factory, candidateId, "Gamma");
        var cvId = await CreateCvAsync(candidateId, positionId);
        var actor = new ActorContext(candidateId, false);

        var version = await GetVersionAsync(cvId);
        var includeA = await Service(_factory).ToggleProjectAsync(actor, cvId, new CvProjectToggleInput(projectIdA, version));
        includeA.Succeeded.Should().BeTrue();
        includeA.Value!.Projects.Single(p => p.ProjectId == projectIdA).IsIncluded.Should().BeTrue();

        var version2 = await GetVersionAsync(cvId);
        var includeB = await Service(_factory).ToggleProjectAsync(actor, cvId, new CvProjectToggleInput(projectIdB, version2));
        includeB.Succeeded.Should().BeTrue();

        var version3 = await GetVersionAsync(cvId);
        var overLimit = await Service(_factory).ToggleProjectAsync(actor, cvId, new CvProjectToggleInput(projectIdC, version3));
        overLimit.Error.Code.Should().Be(ErrorCodes.ValidationFailed);

        var version4 = await GetVersionAsync(cvId);
        var excludeA = await Service(_factory).ToggleProjectAsync(actor, cvId, new CvProjectToggleInput(projectIdA, version4));
        excludeA.Succeeded.Should().BeTrue();
        excludeA.Value!.Projects.Single(p => p.ProjectId == projectIdA).IsIncluded.Should().BeFalse();

        // Project belonging to a different profile is rejected.
        var strangerId = await SeedCandidateAsync(_factory);
        var strangerProject = await SeedProjectAsync(_factory, strangerId, "Foreign");
        var version5 = await GetVersionAsync(cvId);
        var foreign = await Service(_factory).ToggleProjectAsync(actor, cvId, new CvProjectToggleInput(strangerProject, version5));
        foreign.Error.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ToggleProject_bumps_cv_version_and_rejects_stale_version()
    {
        var positionId = await SeedPositionAsync(_factory, isPublic: true, maxProjects: 3);
        var candidateId = await SeedCandidateAsync(_factory);
        var projectId = await SeedProjectAsync(_factory, candidateId, "Alpha");
        var cvId = await CreateCvAsync(candidateId, positionId);
        var actor = new ActorContext(candidateId, false);

        var version = await GetVersionAsync(cvId);
        var toggled = await Service(_factory).ToggleProjectAsync(actor, cvId, new CvProjectToggleInput(projectId, version));
        toggled.Succeeded.Should().BeTrue();

        var bumped = await GetVersionAsync(cvId);
        bumped.Should().BeGreaterThan(version);

        var stale = await Service(_factory).ToggleProjectAsync(actor, cvId, new CvProjectToggleInput(projectId, version));
        stale.Error.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Profile_value_change_refreshes_published_search_text()
    {
        var defId = Guid.NewGuid();
        await SeedDefinitionAsync(_factory, defId, "City", AttributeDataType.String);
        var positionId = await SeedPositionAsync(_factory, isPublic: true,
            attributes: [(defId, "City", AttributeDataType.String, true)]);
        var candidateId = await SeedCandidateAsync(_factory, ("City", AttributeDataType.String, "Warsaw", defId));
        var cvId = await CreateCvAsync(candidateId, positionId);
        var actor = new ActorContext(candidateId, false);

        var version = await GetVersionAsync(cvId);
        (await Service(_factory).PublishAsync(actor, cvId, new CvStatusInput(version))).Succeeded.Should().BeTrue();

        var profileService = new CvPlatform.Application.Profiles.ProfileService(_factory);
        var profile = await profileService.GetForUserAsync(actor, candidateId);
        var existing = profile.Value!.Values.Single(v => v.AttributeDefinitionId == defId);
        var save = await profileService.SaveAttributeValueAsync(actor, candidateId,
            new CvPlatform.Application.Profiles.AttributeValueInput(defId, StringValue: "Krakow", ExpectedVersion: existing.Version));
        save.Succeeded.Should().BeTrue();

        await using var verify = _factory.CreateDbContext();
        var stored = await verify.Cvs.SingleAsync(c => c.Id == cvId);
        stored.SearchText.Should().Contain("Krakow").And.NotContain("Warsaw");
    }

    [Fact]
    public async Task Position_template_edit_refreshes_published_search_text()
    {
        var defId = Guid.NewGuid();
        await SeedDefinitionAsync(_factory, defId, "City", AttributeDataType.String);
        var extraId = await SeedDefinitionAsync(_factory, Guid.NewGuid(), "Note", AttributeDataType.String);
        var ownerId = await SeedUserWithoutProfileAsync(_factory);
        var positionId = await SeedPositionAsync(_factory, isPublic: true, ownerId: ownerId,
            attributes: [(defId, "City", AttributeDataType.String, true)]);
        var candidateId = await SeedCandidateAsync(_factory,
            ("City", AttributeDataType.String, "Warsaw", defId),
            ("Note", AttributeDataType.String, "Hello", extraId));
        var cvId = await CreateCvAsync(candidateId, positionId);
        var actor = new ActorContext(candidateId, false);

        var version = await GetVersionAsync(cvId);
        (await Service(_factory).PublishAsync(actor, cvId, new CvStatusInput(version))).Succeeded.Should().BeTrue();

        await using (var check = _factory.CreateDbContext())
        {
            var before = await check.Cvs.SingleAsync(c => c.Id == cvId);
            before.SearchText.Should().NotContain("Note");
        }

        var positionService = new CvPlatform.Application.Positions.PositionService(_factory);
        var ownerActor = new ActorContext(ownerId, false, true);
        var detail = await positionService.GetForEditAsync(ownerActor, positionId);
        var templateVersion = detail.Value!.Position.Version;
        var saved = await positionService.SaveAttributeAsync(ownerActor, positionId,
            new CvPlatform.Application.Positions.PositionAttributeInput(extraId, IsRequired: false, ExpectedVersion: templateVersion));
        saved.Succeeded.Should().BeTrue();

        await using var verify = _factory.CreateDbContext();
        var after = await verify.Cvs.SingleAsync(c => c.Id == cvId);
        after.SearchText.Should().Contain("Note").And.Contain("Hello");
    }

    [Fact]
    public async Task Delete_is_owner_or_admin_only_and_removes_project_links()
    {
        var positionId = await SeedPositionAsync(_factory, isPublic: true);
        var candidateId = await SeedCandidateAsync(_factory);
        var projectId = await SeedProjectAsync(_factory, candidateId, "Alpha");
        var cvId = await CreateCvAsync(candidateId, positionId);
        var actor = new ActorContext(candidateId, false);

        var version = await GetVersionAsync(cvId);
        await Service(_factory).ToggleProjectAsync(actor, cvId, new CvProjectToggleInput(projectId, version));

        var outsider = await Service(_factory).DeleteAsync(new ActorContext(Guid.NewGuid(), false), cvId);
        outsider.Error.Code.Should().Be(ErrorCodes.Forbidden);

        var admin = await Service(_factory).DeleteAsync(new ActorContext(Guid.NewGuid(), true), cvId);
        admin.Succeeded.Should().BeTrue();

        await using var verify = _factory.CreateDbContext();
        (await verify.Cvs.AnyAsync(c => c.Id == cvId)).Should().BeFalse();
        (await verify.CvProjects.AnyAsync(cp => cp.CvId == cvId)).Should().BeFalse();
        (await verify.Projects.AnyAsync(p => p.Id == projectId)).Should().BeTrue();
    }

    [Fact]
    public async Task ListByPosition_hides_drafts_from_recruiters_but_shares_published_cvs()
    {
        var defId = Guid.NewGuid();
        await SeedDefinitionAsync(_factory, defId, "City", AttributeDataType.String);
        var candidateId = await SeedCandidateAsync(_factory, ("City", AttributeDataType.String, "Warsaw", defId));
        var recruiterId = await SeedUserWithoutProfileAsync(_factory); // position owner (recruiter)
        var positionId = await SeedPositionAsync(_factory, isPublic: false, ownerId: recruiterId,
            attributes: [(defId, "City", AttributeDataType.String, true)],
            rules: [(defId, RuleOperator.Equals, "Warsaw")]);
        var adminId = Guid.NewGuid();
        var cvId = await CreateCvAsync(candidateId, positionId);
        var actor = new ActorContext(candidateId, false);

        // Third parties cannot browse at all; the owner just sees no CVs yet.
        var stranger = await Service(_factory).ListByPositionAsync(new ActorContext(Guid.NewGuid(), false), positionId);
        stranger.Error.Code.Should().Be(ErrorCodes.Forbidden);
        var empty = await Service(_factory).ListByPositionAsync(new ActorContext(recruiterId, false, true), positionId);
        empty.Succeeded.Should().BeTrue();
        empty.Value!.Should().BeEmpty();

        // Publish the CV.
        var version = await GetVersionAsync(cvId);
        (await Service(_factory).PublishAsync(actor, cvId, new CvStatusInput(version))).Succeeded.Should().BeTrue();

        // Recruiter (position owner) now sees it.
        var visible = await Service(_factory).ListByPositionAsync(new ActorContext(recruiterId, false, true), positionId);
        visible.Succeeded.Should().BeTrue();
        visible.Value!.Select(c => c.Id).Should().Contain(cvId);

        // Candidate loses access (City changed); shared recruiters still manage the position CV.
        await using (var db = _factory.CreateDbContext())
        {
            var value = await db.ProfileAttributeValues.SingleAsync(
                v => v.Profile.UserId == candidateId && v.AttributeDefinitionId == defId);
            db.ProfileAttributeValues.Remove(value);
            await db.SaveChangesAsync();
        }

        var hidden = await Service(_factory).ListByPositionAsync(new ActorContext(recruiterId, false, true), positionId);
        hidden.Succeeded.Should().BeTrue();
        hidden.Value!.Select(c => c.Id).Should().Contain(cvId);

        // Admin still sees everything.
        var admin = await Service(_factory).ListByPositionAsync(new ActorContext(adminId, true), positionId);
        admin.Value!.Select(c => c.Id).Should().Contain(cvId);
    }

    [Fact]
    public async Task ListMine_returns_only_own_cvs()
    {
        var positionId = await SeedPositionAsync(_factory, isPublic: true);
        var candidateId = await SeedCandidateAsync(_factory);
        var otherId = await SeedCandidateAsync(_factory);
        await CreateCvAsync(candidateId, positionId);
        await CreateCvAsync(otherId, positionId);

        var mine = await Service(_factory).ListMineAsync(new ActorContext(candidateId, false));

        mine.Value!.Should().ContainSingle().Which.UserId.Should().Be(candidateId);
    }

    [Fact]
    public async Task GetRendered_hides_draft_from_third_party_but_admin_sees_it()
    {
        var positionId = await SeedPositionAsync(_factory, isPublic: true);
        var candidateId = await SeedCandidateAsync(_factory);
        var cvId = await CreateCvAsync(candidateId, positionId);

        var stranger = await Service(_factory).GetRenderedAsync(new ActorContext(Guid.NewGuid(), false), cvId);
        stranger.Error.Code.Should().Be(ErrorCodes.Forbidden);

        var admin = await Service(_factory).GetRenderedAsync(new ActorContext(Guid.NewGuid(), true), cvId);
        admin.Succeeded.Should().BeTrue();
        admin.Value!.Cv.UserId.Should().Be(candidateId);
    }

    private async Task<Guid> CreateCvAsync(Guid candidateId, Guid positionId)
    {
        var result = await Service(_factory).CreateAsync(new ActorContext(candidateId, false), positionId);
        result.Succeeded.Should().BeTrue($"CV creation is a precondition here (got: {result.Error.Code}).");
        return result.Value!.Id;
    }

    private async Task<long> GetVersionAsync(Guid cvId)
    {
        await using var db = _factory.CreateDbContext();
        return (await db.Cvs.SingleAsync(c => c.Id == cvId)).Version;
    }

    private static async Task<Guid> SeedUserWithoutProfileAsync(IAppDbContextFactory factory)
    {
        await using var db = factory.CreateDbContext();
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = $"u{userId:N}@example.test" });
        await db.SaveChangesAsync();
        return userId;
    }

    private static async Task<Guid> SeedDefinitionAsync(
        IAppDbContextFactory factory, Guid definitionId, string name, AttributeDataType type)
    {
        await using var db = factory.CreateDbContext();
        db.AttributeCategories.Add(new AttributeCategory
        {
            Id = Guid.NewGuid(),
            Name = name,
            Definitions =
            [
                new AttributeDefinition
                {
                    Id = definitionId,
                    Name = name,
                    DataType = type,
                },
            ],
        });
        await db.SaveChangesAsync();
        return definitionId;
    }

    private static async Task<Guid> SeedPositionAsync(
        IAppDbContextFactory factory, bool isPublic, int maxProjects = 3, Guid? ownerId = null,
        List<(Guid DefinitionId, string Name, AttributeDataType Type, bool IsRequired)>? attributes = null,
        List<(Guid DefinitionId, RuleOperator Op, string Compare)>? rules = null)
    {
        await using var db = factory.CreateDbContext();
        var positionId = Guid.NewGuid();
        var position = new Position
        {
            Id = positionId,
            OwnerId = ownerId,
            Title = "Lead",
            IsPublic = isPublic,
            MaxProjects = maxProjects,
        };
        foreach (var (definitionId, name, type, isRequired) in attributes ?? [])
        {
            position.Attributes.Add(new PositionAttribute
            {
                PositionId = positionId,
                AttributeDefinitionId = definitionId,
                IsRequired = isRequired,
                SortOrder = position.Attributes.Count,
            });
        }
        foreach (var (definitionId, op, compare) in rules ?? [])
        {
            position.AccessRules.Add(new AccessRule
            {
                Id = Guid.NewGuid(),
                AttributeDefinitionId = definitionId,
                DataType = AttributeDataType.String,
                Operator = op,
                ComparisonValue = compare,
            });
        }
        db.Positions.Add(position);
        await db.SaveChangesAsync();
        return positionId;
    }

    private static async Task<Guid> SeedCandidateAsync(
        IAppDbContextFactory factory,
        params (string Name, AttributeDataType Type, string? Value, Guid DefinitionId)[] values)
    {
        await using var db = factory.CreateDbContext();
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = $"c{userId:N}@example.test" });
        db.Profiles.Add(new Profile { Id = profileId, UserId = userId });
        foreach (var (name, type, value, definitionId) in values)
        {
            if (!await db.AttributeDefinitions.AnyAsync(d => d.Id == definitionId))
            {
                db.AttributeCategories.Add(new AttributeCategory
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Definitions =
                    [
                        new AttributeDefinition
                        {
                            Id = definitionId,
                            Name = name,
                            DataType = type,
                        },
                    ],
                });
            }
            db.ProfileAttributeValues.Add(new ProfileAttributeValue
            {
                ProfileId = profileId,
                AttributeDefinitionId = definitionId,
                StringValue = value,
            });
        }
        await db.SaveChangesAsync();
        return userId;
    }

    private static async Task SeedValueAsync(
        IAppDbContextFactory factory, Guid userId, Guid definitionId, string stringValue)
    {
        await using var db = factory.CreateDbContext();
        var profileId = await db.Profiles.Where(p => p.UserId == userId).Select(p => p.Id).SingleAsync();
        db.ProfileAttributeValues.Add(new ProfileAttributeValue
        {
            ProfileId = profileId,
            AttributeDefinitionId = definitionId,
            StringValue = stringValue,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedProjectAsync(IAppDbContextFactory factory, Guid userId, string name)
    {
        await using var db = factory.CreateDbContext();
        var projectId = Guid.NewGuid();
        var profileId = await db.Profiles.Where(p => p.UserId == userId).Select(p => p.Id).SingleAsync();
        db.Projects.Add(new Project { Id = projectId, ProfileId = profileId, Name = name });
        await db.SaveChangesAsync();
        return projectId;
    }
}
