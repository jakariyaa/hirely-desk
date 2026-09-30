using AwesomeAssertions;
using CvPlatform.Application.Attributes;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
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

    [Fact]
    public async Task ListAsync_sorts_by_requested_field_and_direction()
    {
        var (service, _) = await CreateServiceAsync();
        var actor = new ActorContext(Guid.NewGuid(), false, true);
        var page = new PageRequest(1, 10);

        var byName = await service.ListAsync(actor, new AttributeCatalogQuery(Page: page));
        byName.Value!.Items.Select(i => i.Name).Should().Equal("Alpha", "Beta", "Gamma");

        var descending = await service.ListAsync(actor,
            new AttributeCatalogQuery(Page: page, Sort: new AttributeSort(AttributeSortField.Name, true)));
        descending.Value!.Items.Select(i => i.Name).Should().Equal("Gamma", "Beta", "Alpha");

        var byCategory = await service.ListAsync(actor,
            new AttributeCatalogQuery(Page: page, Sort: new AttributeSort(AttributeSortField.Category, false)));
        byCategory.Value!.Items.Select(i => i.CategoryName).Should().Equal("Personal", "Skills", "Skills");

        var byCategoryDescending = await service.ListAsync(actor,
            new AttributeCatalogQuery(Page: page, Sort: new AttributeSort(AttributeSortField.Category, true)));
        byCategoryDescending.Value!.Items.Select(i => i.CategoryName).Should().Equal("Skills", "Skills", "Personal");

        var byType = await service.ListAsync(actor,
            new AttributeCatalogQuery(Page: page, Sort: new AttributeSort(AttributeSortField.DataType, false)));
        byType.Value!.Items.Select(i => i.DataType).Should().Equal(
            AttributeDataType.String, AttributeDataType.Numeric, AttributeDataType.Dropdown);
    }

    [Fact]
    public async Task ListAsync_filters_by_category_and_search()
    {
        var (service, contextFactory) = await CreateServiceAsync();
        var actor = new ActorContext(Guid.NewGuid(), false, true);
        await using var db = contextFactory.CreateDbContext();
        var skillsId = await db.AttributeCategories.Where(c => c.Name == "Skills").Select(c => c.Id).SingleAsync();

        var byCategory = await service.ListAsync(actor,
            new AttributeCatalogQuery(CategoryId: skillsId, Page: new PageRequest(1, 10)));
        byCategory.Value!.TotalCount.Should().Be(2);
        byCategory.Value.Items.Select(i => i.Name).Should().BeEquivalentTo(["Alpha", "Beta"]);

        var bySearch = await service.ListAsync(actor,
            new AttributeCatalogQuery(Search: "Beta", Page: new PageRequest(1, 10)));
        bySearch.Value!.TotalCount.Should().Be(1);
        bySearch.Value.Items[0].Name.Should().Be("Beta");
    }

    [Fact]
    public async Task DeleteManyAsync_rejects_the_whole_batch_when_a_built_in_attribute_is_selected()
    {
        var (service, contextFactory) = await CreateServiceAsync();
        await using var db = contextFactory.CreateDbContext();
        var builtIn = await db.AttributeDefinitions.SingleAsync(d => d.Name == "Alpha");
        builtIn.IsBuiltIn = true;
        await db.SaveChangesAsync();

        await using var scope = contextFactory.CreateDbContext();
        var other = await scope.AttributeDefinitions.SingleAsync(d => d.Name == "Beta");
        var actor = new ActorContext(Guid.NewGuid(), true);
        var result = await service.DeleteManyAsync(actor,
        [
            new AttributeDefinitionDeleteInput(builtIn.Id, builtIn.Version),
            new AttributeDefinitionDeleteInput(other.Id, other.Version),
        ]);

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.Forbidden);
        await using var verify = contextFactory.CreateDbContext();
        (await verify.AttributeDefinitions.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task DeleteManyAsync_rejects_the_whole_batch_on_version_conflict()
    {
        var (service, contextFactory) = await CreateServiceAsync();
        await using var db = contextFactory.CreateDbContext();
        var first = await db.AttributeDefinitions.SingleAsync(d => d.Name == "Alpha");
        var second = await db.AttributeDefinitions.SingleAsync(d => d.Name == "Beta");

        var actor = new ActorContext(Guid.NewGuid(), true);
        var result = await service.DeleteManyAsync(actor,
        [
            new AttributeDefinitionDeleteInput(first.Id, first.Version),
            new AttributeDefinitionDeleteInput(second.Id, second.Version + 5),
        ]);

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
        await using var verify = contextFactory.CreateDbContext();
        (await verify.AttributeDefinitions.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task DeleteManyAsync_removes_every_requested_attribute()
    {
        var (service, contextFactory) = await CreateServiceAsync();
        await using var db = contextFactory.CreateDbContext();
        var alpha = await db.AttributeDefinitions.SingleAsync(d => d.Name == "Alpha");
        var gamma = await db.AttributeDefinitions.SingleAsync(d => d.Name == "Gamma");

        var actor = new ActorContext(Guid.NewGuid(), true);
        var result = await service.DeleteManyAsync(actor,
        [
            new AttributeDefinitionDeleteInput(alpha.Id, alpha.Version),
            new AttributeDefinitionDeleteInput(gamma.Id, gamma.Version),
        ]);

        result.Succeeded.Should().BeTrue();
        await using var verify = contextFactory.CreateDbContext();
        (await verify.AttributeDefinitions.Select(d => d.Name).ToListAsync()).Should().BeEquivalentTo(["Beta"]);
    }

    [Fact]
    public async Task GetDeleteImpactAsync_aggregates_a_batch_and_ignores_missing_ids()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var provider = services.BuildServiceProvider();
        var efFactory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var service = new AttributeDefinitionService(new TestFactory(efFactory));
        await using var db = efFactory.CreateDbContext();
        var definition = new AttributeDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Score",
            DataType = AttributeDataType.Numeric,
            Category = new AttributeCategory { Id = Guid.NewGuid(), Name = "Skills" },
        };
        var position = Position(false, definition.Id, Guid.NewGuid());
        db.AttributeDefinitions.Add(definition);
        db.Positions.Add(position);
        await db.SaveChangesAsync();
        var actor = new ActorContext(Guid.NewGuid(), true);

        var single = await service.GetDeleteImpactAsync(actor, [definition.Id, Guid.NewGuid()]);
        var batch = await service.GetDeleteImpactAsync(actor, [definition.Id]);

        single.Succeeded.Should().BeFalse();
        single.Error.Code.Should().Be(ErrorCodes.NotFound);
        batch.Succeeded.Should().BeTrue();
        batch.Value.Should().Be(new AttributeDeleteImpactDto(0, 1, 1, 0, 1));
        (await service.GetDeleteImpactAsync(actor, [])).Value.Should().Be(AttributeDeleteImpactDto.Zero);
    }

    private static async Task<(AttributeDefinitionService Service, IDbContextFactory<AppDbContext> Factory)> CreateServiceAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var provider = services.BuildServiceProvider();
        var efFactory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var factory = new TestFactory(efFactory);
        await using var db = efFactory.CreateDbContext();
        var skills = new AttributeCategory { Id = Guid.NewGuid(), Name = "Skills" };
        var personal = new AttributeCategory { Id = Guid.NewGuid(), Name = "Personal" };
        db.AttributeCategories.AddRange(skills, personal);
        db.AttributeDefinitions.AddRange(
            new AttributeDefinition { Id = Guid.NewGuid(), Name = "Alpha", DataType = AttributeDataType.String, Category = skills },
            new AttributeDefinition { Id = Guid.NewGuid(), Name = "Beta", DataType = AttributeDataType.Numeric, Category = skills },
            new AttributeDefinition { Id = Guid.NewGuid(), Name = "Gamma", DataType = AttributeDataType.Dropdown, Category = personal });
        await db.SaveChangesAsync();
        return (new AttributeDefinitionService(factory), efFactory);
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
