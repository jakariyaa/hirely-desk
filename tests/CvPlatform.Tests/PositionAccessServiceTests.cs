using AwesomeAssertions;
using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Core.Access;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using CvPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class PositionAccessServiceTests
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

    private static IAccessRuleEngine Engine() => new AccessRuleEngine();

    [Fact]
    public async Task Public_position_grants_access_without_rules()
    {
        var factory = CreateFactory();
        var positionId = await SeedPositionAsync(factory, isPublic: true);
        var candidateId = await SeedCandidateAsync(factory, ("City", AttributeDataType.String, "Warsaw", null));

        var result = await new PositionAccessService(factory, Engine()).CanAccessAsync(
            new ActorContext(candidateId, false), candidateId, positionId);

        result.Succeeded.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task Restricted_position_with_matching_value_grants_access()
    {
        var factory = CreateFactory();
        var definitionId = Guid.NewGuid();
        var positionId = await SeedPositionAsync(factory, isPublic: false,
            rules: [("City", AttributeDataType.String, definitionId, RuleOperator.Equals, "Warsaw")]);
        var candidateId = await SeedCandidateAsync(factory, ("City", AttributeDataType.String, "Warsaw", definitionId));

        var result = await new PositionAccessService(factory, Engine()).CanAccessAsync(
            new ActorContext(candidateId, false), candidateId, positionId);

        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task Restricted_position_with_missing_value_denies_access()
    {
        var factory = CreateFactory();
        var definitionId = Guid.NewGuid();
        var positionId = await SeedPositionAsync(factory, isPublic: false,
            rules: [("City", AttributeDataType.String, definitionId, RuleOperator.Equals, "Warsaw")]);
        var candidateId = await SeedCandidateAsync(factory); // no values at all

        var result = await new PositionAccessService(factory, Engine()).CanAccessAsync(
            new ActorContext(candidateId, false), candidateId, positionId);

        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task Admin_bypasses_restricted_position()
    {
        var factory = CreateFactory();
        var definitionId = Guid.NewGuid();
        var positionId = await SeedPositionAsync(factory, isPublic: false,
            rules: [("City", AttributeDataType.String, definitionId, RuleOperator.Equals, "Warsaw")]);
        var candidateId = await SeedCandidateAsync(factory);

        var result = await new PositionAccessService(factory, Engine()).CanAccessAsync(
            new ActorContext(Guid.NewGuid(), true), candidateId, positionId);

        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_position_returns_not_found()
    {
        var factory = CreateFactory();
        var candidateId = await SeedCandidateAsync(factory);

        var result = await new PositionAccessService(factory, Engine()).CanAccessAsync(
            new ActorContext(candidateId, false), candidateId, Guid.NewGuid());

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Browse_returns_public_owned_and_rule_passing_only()
    {
        var factory = CreateFactory();
        var definitionId = Guid.NewGuid();
        var service = new PositionAccessService(factory, Engine());

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

        var candidateId = await SeedCandidateAsync(factory, ("City", AttributeDataType.String, "Warsaw", definitionId));
        var actor = new ActorContext(candidateId, false);

        Guid publicId, restrictedOkId, restrictedDeniedId, ownedId;
        await using (var db = factory.CreateDbContext())
        {
            publicId = Guid.NewGuid();
            restrictedOkId = Guid.NewGuid();
            restrictedDeniedId = Guid.NewGuid();
            ownedId = Guid.NewGuid();
            db.Positions.Add(new Position { Id = publicId, Title = "Public", IsPublic = true });
            db.Positions.Add(new Position
            {
                Id = restrictedOkId,
                Title = "RestrictedOk",
                IsPublic = false,
                AccessRules =
                [
                    new AccessRule
                    {
                        Id = Guid.NewGuid(),
                        AttributeDefinitionId = definitionId,
                        DataType = AttributeDataType.String,
                        Operator = RuleOperator.Equals,
                        ComparisonValue = "Warsaw",
                    },
                ],
            });
            db.Positions.Add(new Position
            {
                Id = restrictedDeniedId,
                Title = "RestrictedDenied",
                IsPublic = false,
                AccessRules =
                [
                    new AccessRule
                    {
                        Id = Guid.NewGuid(),
                        AttributeDefinitionId = definitionId,
                        DataType = AttributeDataType.String,
                        Operator = RuleOperator.Equals,
                        ComparisonValue = "Krakow",
                    },
                ],
            });
            db.Positions.Add(new Position { Id = ownedId, Title = "Owned", IsPublic = false, OwnerId = candidateId });
            await db.SaveChangesAsync();
        }

        var result = await service.BrowseAsync(actor, new PageRequest(1, 20));

        result.Succeeded.Should().BeTrue();
        result.Value!.Items.Select(p => p.Id).Should().Contain([publicId, restrictedOkId, ownedId]);
        result.Value.Items.Select(p => p.Id).Should().NotContain(restrictedDeniedId);
    }

    [Fact]
    public async Task Public_browse_excludes_restricted_positions_without_an_actor()
    {
        var factory = CreateFactory();
        var service = new PositionAccessService(factory, Engine());
        var publicId = await SeedPositionAsync(factory, isPublic: true);
        await SeedPositionAsync(factory, isPublic: false);

        var result = await service.BrowsePublicAsync(new PageRequest(1, 20));

        result.Succeeded.Should().BeTrue();
        result.Value!.Items.Select(p => p.Id).Should().ContainSingle().Which.Should().Be(publicId);
    }

    [Fact]
    public async Task GetAccessible_returns_forbidden_when_rules_fail()
    {
        var factory = CreateFactory();
        var definitionId = Guid.NewGuid();
        var service = new PositionAccessService(factory, Engine());
        await using (var db = factory.CreateDbContext())
        {
            db.AttributeDefinitions.Add(new AttributeDefinition
            {
                Id = definitionId,
                CategoryId = Guid.NewGuid(),
                Name = "City",
                DataType = AttributeDataType.String,
            });
            var position = new Position { Id = Guid.NewGuid(), Title = "Restricted", IsPublic = false };
            position.AccessRules.Add(new AccessRule
            {
                Id = Guid.NewGuid(),
                AttributeDefinitionId = definitionId,
                DataType = AttributeDataType.String,
                Operator = RuleOperator.Equals,
                ComparisonValue = "Warsaw",
            });
            db.Positions.Add(position);
            await db.SaveChangesAsync();

            var candidateId = await SeedCandidateAsync(factory);
            var result = await service.GetAccessibleAsync(new ActorContext(candidateId, false), position.Id);

            result.Succeeded.Should().BeFalse();
            result.Error.Code.Should().Be(ErrorCodes.Forbidden);
        }
    }

    private static async Task<Guid> SeedPositionAsync(
        IAppDbContextFactory factory, bool isPublic,
        List<(string Name, AttributeDataType Type, Guid DefinitionId, RuleOperator Op, string Compare)>? rules = null)
    {
        await using var db = factory.CreateDbContext();
        var positionId = Guid.NewGuid();
        var position = new Position { Id = positionId, Title = "Lead", IsPublic = isPublic };
        foreach (var rule in rules ?? [])
        {
            db.AttributeDefinitions.Add(new AttributeDefinition
            {
                Id = rule.DefinitionId,
                CategoryId = Guid.NewGuid(),
                Name = rule.Name,
                DataType = rule.Type,
            });
            position.AccessRules.Add(new AccessRule
            {
                Id = Guid.NewGuid(),
                AttributeDefinitionId = rule.DefinitionId,
                DataType = rule.Type,
                Operator = rule.Op,
                ComparisonValue = rule.Compare,
            });
        }
        db.Positions.Add(position);
        await db.SaveChangesAsync();
        return positionId;
    }

    private static async Task<Guid> SeedCandidateAsync(
        IAppDbContextFactory factory,
        params (string Name, AttributeDataType Type, string? Value, Guid? DefinitionId)[] values)
    {
        await using var db = factory.CreateDbContext();
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = $"c{userId:N}@example.test" });
        db.Profiles.Add(new Profile { Id = profileId, UserId = userId });
        foreach (var (name, type, value, definitionId) in values)
        {
            var id = definitionId ?? Guid.NewGuid();
            if (definitionId is null)
            {
                db.AttributeDefinitions.Add(new AttributeDefinition
                {
                    Id = id,
                    CategoryId = Guid.NewGuid(),
                    Name = name,
                    DataType = type,
                });
            }
            db.ProfileAttributeValues.Add(new ProfileAttributeValue
            {
                ProfileId = profileId,
                AttributeDefinitionId = id,
                StringValue = value,
            });
        }
        await db.SaveChangesAsync();
        return userId;
    }
}
