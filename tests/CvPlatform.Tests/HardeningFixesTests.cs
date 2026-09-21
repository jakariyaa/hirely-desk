using AwesomeAssertions;
using CvPlatform.Application.Access;
using CvPlatform.Application.Attributes;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Positions;
using CvPlatform.Core.Access;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using CvPlatform.Infrastructure.Data;
using CvPlatform.Infrastructure.Markdown;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class HardeningFixesTests
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
    public void MarkdownRenderer_strips_script_and_javascript_urls()
    {
        var renderer = new MarkdownRenderer();
        var html = renderer.ToSafeHtml("Hello **world**\n\n<script>alert(1)</script>\n\n[click](javascript:alert(1))");
        html.Should().Contain("<strong>world</strong>");
        html.Should().NotContain("<script");
        html.Should().NotContain("javascript:");
    }

    [Fact]
    public void MarkdownRenderer_encodes_raw_html()
    {
        var renderer = new MarkdownRenderer();
        var html = renderer.ToSafeHtml("<img src=x onerror=alert(1)>");
        html.Should().NotContain("<img");
        html.Should().Contain("&lt;img");
    }

    [Fact]
    public void Text_contains_rule_uses_text_value()
    {
        var attributeId = Guid.NewGuid();
        var position = new Position
        {
            Id = Guid.NewGuid(),
            IsPublic = false,
            AccessRules =
            [
                new AccessRule
                {
                    Id = Guid.NewGuid(),
                    AttributeDefinitionId = attributeId,
                    DataType = AttributeDataType.Text,
                    Operator = RuleOperator.Contains,
                    ComparisonValue = "postgres",
                },
            ],
        };
        var engine = new AccessRuleEngine();
        var values = new Dictionary<Guid, TypedValue>
        {
            [attributeId] = new(AttributeDataType.Text, TextValue: "I love Postgres and EF Core"),
        };
        engine.CanAccess(position, false, values).Should().BeTrue();
    }

    [Fact]
    public async Task CanAccessMany_matches_single_checks_without_n_plus_one()
    {
        var factory = CreateFactory();
        var definitionId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.AttributeDefinitions.Add(new AttributeDefinition
            {
                Id = definitionId,
                CategoryId = Guid.NewGuid(),
                Name = "City",
                DataType = AttributeDataType.String,
            });
            await db.SaveChangesAsync();
        }

        var candidateOk = await SeedCandidateAsync(factory, definitionId, "Warsaw");
        var candidateDenied = await SeedCandidateAsync(factory, definitionId, "Krakow");
        var restricted = await SeedRestrictedPositionAsync(factory, definitionId, "Warsaw");
        var pub = await SeedPublicPositionAsync(factory);
        var service = new PositionAccessService(factory, new AccessRuleEngine());
        var actor = new ActorContext(candidateOk, false);

        var batch = await service.CanAccessManyAsync(actor,
            [(candidateOk, restricted), (candidateDenied, restricted), (candidateOk, pub)]);
        batch.Succeeded.Should().BeTrue();
        batch.Value![(candidateOk, restricted)].Should().BeTrue();
        batch.Value[(candidateDenied, restricted)].Should().BeFalse();
        batch.Value[(candidateOk, pub)].Should().BeTrue();
    }

    [Fact]
    public async Task Position_delete_impact_counts_cvs_discussions_and_likes()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(new ApplicationUser { Id = ownerId, UserName = "owner@test" });
            db.Users.Add(new ApplicationUser { Id = candidateId, UserName = "cand@test" });
            db.Profiles.Add(new Profile { Id = profileId, UserId = candidateId });
            db.Positions.Add(new Position { Id = positionId, Title = "T", IsPublic = true, OwnerId = ownerId });
            var cvId = Guid.NewGuid();
            db.Cvs.Add(new Cv { Id = cvId, ProfileId = profileId, PositionId = positionId });
            db.DiscussionPosts.Add(new DiscussionPost
            {
                Id = Guid.NewGuid(),
                PositionId = positionId,
                AuthorId = ownerId,
                TextMarkdown = "hi",
            });
            db.CvLikes.Add(new CvLike { CvId = cvId, RecruiterId = ownerId });
            await db.SaveChangesAsync();
        }

        var service = new PositionService(factory);
        var impact = await service.GetDeleteImpactAsync(new ActorContext(ownerId, false, true), positionId);
        impact.Succeeded.Should().BeTrue();
        impact.Value!.Cvs.Should().Be(1);
        impact.Value.DiscussionPosts.Should().Be(1);
        impact.Value.Likes.Should().Be(1);
    }

    [Fact]
    public async Task Attribute_name_conflict_is_case_insensitive()
    {
        var factory = CreateFactory();
        var categoryId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.AttributeCategories.Add(new AttributeCategory { Id = categoryId, Name = "General" });
            db.AttributeDefinitions.Add(new AttributeDefinition
            {
                Id = Guid.NewGuid(),
                CategoryId = categoryId,
                Name = "City",
                DataType = AttributeDataType.String,
            });
            await db.SaveChangesAsync();
        }

        var service = new AttributeDefinitionService(factory);
        var admin = new ActorContext(Guid.NewGuid(), true);
        var duplicate = await service.CreateAsync(admin,
            new AttributeDefinitionInput(categoryId, "  CITY ", null, AttributeDataType.String, null));
        duplicate.Succeeded.Should().BeFalse();
        duplicate.Error.Code.Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task SearchText_only_refresh_does_not_bump_cv_version()
    {
        var factory = CreateFactory();
        var cvId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Cvs.Add(new Cv { Id = cvId, ProfileId = Guid.NewGuid(), PositionId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }

        long versionBefore;
        await using (var db = factory.CreateDbContext())
        {
            var cv = await db.Cvs.SingleAsync(c => c.Id == cvId);
            versionBefore = cv.Version;
            cv.SearchText = "refreshed index text";
            await db.SaveChangesAsync();
        }

        await using (var db = factory.CreateDbContext())
        {
            var cv = await db.Cvs.AsNoTracking().SingleAsync(c => c.Id == cvId);
            cv.Version.Should().Be(versionBefore);
            cv.SearchText.Should().Be("refreshed index text");
        }
    }

    private static async Task<Guid> SeedCandidateAsync(
        IAppDbContextFactory factory, Guid definitionId, string city)
    {
        await using var db = factory.CreateDbContext();
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = $"c{userId:N}@example.test" });
        db.Profiles.Add(new Profile { Id = profileId, UserId = userId });
        db.ProfileAttributeValues.Add(new ProfileAttributeValue
        {
            ProfileId = profileId,
            AttributeDefinitionId = definitionId,
            StringValue = city,
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private static async Task<Guid> SeedRestrictedPositionAsync(
        IAppDbContextFactory factory, Guid definitionId, string city)
    {
        await using var db = factory.CreateDbContext();
        var positionId = Guid.NewGuid();
        db.Positions.Add(new Position
        {
            Id = positionId,
            Title = "Restricted",
            IsPublic = false,
            AccessRules =
            [
                new AccessRule
                {
                    Id = Guid.NewGuid(),
                    AttributeDefinitionId = definitionId,
                    DataType = AttributeDataType.String,
                    Operator = RuleOperator.Equals,
                    ComparisonValue = city,
                },
            ],
        });
        await db.SaveChangesAsync();
        return positionId;
    }

    private static async Task<Guid> SeedPublicPositionAsync(IAppDbContextFactory factory)
    {
        await using var db = factory.CreateDbContext();
        var positionId = Guid.NewGuid();
        db.Positions.Add(new Position { Id = positionId, Title = "Public", IsPublic = true });
        await db.SaveChangesAsync();
        return positionId;
    }
}
