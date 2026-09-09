using AwesomeAssertions;
using CvPlatform.Application.Attributes;
using CvPlatform.Application.Authorization;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using CvPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class AttributeDefinitionServiceTests
{
    [Fact]
    public async Task GetDeleteImpactAsync_counts_only_restricted_positions_losing_all_rules()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var provider = services.BuildServiceProvider();
        var efFactory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var factory = new TestFactory(efFactory);
        await using var db = efFactory.CreateDbContext();
        var definition = new AttributeDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Score",
            DataType = AttributeDataType.Numeric,
            Category = new AttributeCategory { Id = Guid.NewGuid(), Name = "Skills" },
        };
        var publicPosition = Position(true, definition.Id, Guid.NewGuid());
        var partiallyGated = Position(false, definition.Id, Guid.NewGuid());
        partiallyGated.AccessRules.Add(new AccessRule
        {
            Id = Guid.NewGuid(),
            AttributeDefinitionId = Guid.NewGuid(),
            DataType = AttributeDataType.String,
            Operator = RuleOperator.Equals,
            ComparisonValue = "x",
        });
        var fullyGated = Position(false, definition.Id, Guid.NewGuid());
        db.AttributeDefinitions.Add(definition);
        db.Positions.AddRange(publicPosition, partiallyGated, fullyGated);
        await db.SaveChangesAsync();

        var result = await new AttributeDefinitionService(factory)
            .GetDeleteImpactAsync(new ActorContext(Guid.NewGuid(), true), definition.Id);

        result.Succeeded.Should().BeTrue();
        result.Value!.RestrictedPositionsLosingGating.Should().Be(1);
    }

    private static Position Position(bool isPublic, Guid attributeId, Guid ruleId) => new()
    {
        Id = Guid.NewGuid(),
        Title = Guid.NewGuid().ToString(),
        IsPublic = isPublic,
        Attributes = [new PositionAttribute { AttributeDefinitionId = attributeId }],
        AccessRules =
        [
            new AccessRule
            {
                Id = ruleId,
                AttributeDefinitionId = attributeId,
                DataType = AttributeDataType.Numeric,
                Operator = RuleOperator.GreaterThan,
                ComparisonValue = "1",
            },
        ],
    };

    private sealed class TestFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
    {
        public IAppDbContext CreateDbContext() => factory.CreateDbContext();
    }
}
