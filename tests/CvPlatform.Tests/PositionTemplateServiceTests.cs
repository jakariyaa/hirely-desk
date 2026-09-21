using AwesomeAssertions;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Attributes;
using CvPlatform.Application.Positions;
using CvPlatform.Application.PositionTemplates;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class PositionTemplateServiceTests
{
    private sealed class TestFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
    {
        public IAppDbContext CreateDbContext() => factory.CreateDbContext();
    }

    private static IAppDbContextFactory CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        return new TestFactory(services.BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>());
    }

    [Fact]
    public async Task Recruiters_share_template_crud_and_creation()
    {
        var factory = CreateFactory();
        var service = new PositionTemplateService(factory);
        var firstRecruiter = new ActorContext(Guid.NewGuid(), false, true);
        var secondRecruiter = new ActorContext(Guid.NewGuid(), false, true);
        var definitionId = await SeedDefinitionAsync(factory);

        var created = await service.CreateAsync(firstRecruiter,
            new PositionTemplateInput("Backend", "Build services", "Acme", "Senior",
                Attributes: [new AttributeRequirementInput(definitionId, true, 0)]));

        created.Succeeded.Should().BeTrue();
        var listed = await service.ListAsync(secondRecruiter, new PageRequest(1, 10));
        listed.Value!.Items.Should().ContainSingle(t => t.Id == created.Value!.Id);

        var position = await service.CreatePositionAsync(secondRecruiter, created.Value!.Id);

        position.Succeeded.Should().BeTrue();
        position.Value!.Title.Should().Be("Backend");
        position.Value.OwnerId.Should().Be(secondRecruiter.UserId);

        var detail = await service.GetAsync(secondRecruiter, created.Value.Id);
        detail.Value!.Attributes.Should().ContainSingle(attribute =>
            attribute.AttributeDefinitionId == definitionId && attribute.IsRequired && attribute.SortOrder == 0);

        var positionService = new PositionService(factory);
        var positionDetail = await positionService.GetForEditAsync(secondRecruiter, position.Value.Id);
        positionDetail.Value!.Attributes.Should().ContainSingle(attribute =>
            attribute.AttributeDefinitionId == definitionId && attribute.IsRequired && attribute.SortOrder == 0);
    }

    [Fact]
    public async Task Candidates_cannot_manage_templates()
    {
        var service = new PositionTemplateService(CreateFactory());

        var result = await service.CreateAsync(
            new ActorContext(Guid.NewGuid(), false, false, true),
            new PositionTemplateInput("Backend", "", null, null));

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task UpdateAsync_replaces_attribute_requirements()
    {
        var factory = CreateFactory();
        var service = new PositionTemplateService(factory);
        var actor = new ActorContext(Guid.NewGuid(), false, true);
        var definitionId = await SeedDefinitionAsync(factory);
        var created = await service.CreateAsync(actor,
            new PositionTemplateInput("Backend", "", null, null,
                Attributes: [new AttributeRequirementInput(definitionId, true, 0)]));

        var updated = await service.UpdateAsync(actor, created.Value!.Id,
            new PositionTemplateInput("Backend", "", null, null,
                ExpectedVersion: created.Value.Version,
                Attributes: [new AttributeRequirementInput(definitionId, false, 0)]));

        updated.Succeeded.Should().BeTrue();
        var detail = await service.GetAsync(actor, created.Value.Id);
        detail.Value!.Attributes.Should().ContainSingle(attribute =>
            attribute.AttributeDefinitionId == definitionId && !attribute.IsRequired);
    }

    [Fact]
    public async Task DeleteManyAsync_deletes_all_selected_templates()
    {
        var factory = CreateFactory();
        var service = new PositionTemplateService(factory);
        var actor = new ActorContext(Guid.NewGuid(), false, true);
        var first = await service.CreateAsync(actor, new PositionTemplateInput("Backend", "", null, null));
        var second = await service.CreateAsync(actor, new PositionTemplateInput("Frontend", "", null, null));

        var result = await service.DeleteManyAsync(actor,
        [
            new PositionTemplateDeleteInput(first.Value!.Id, first.Value.Version),
            new PositionTemplateDeleteInput(second.Value!.Id, second.Value.Version),
        ]);

        result.Succeeded.Should().BeTrue();
        var listed = await service.ListAsync(actor, new PageRequest(1, 10));
        listed.Value!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteManyAsync_rejects_stale_version_without_partial_deletion()
    {
        var factory = CreateFactory();
        var service = new PositionTemplateService(factory);
        var actor = new ActorContext(Guid.NewGuid(), false, true);
        var first = await service.CreateAsync(actor, new PositionTemplateInput("Backend", "", null, null));
        var second = await service.CreateAsync(actor, new PositionTemplateInput("Frontend", "", null, null));

        var result = await service.DeleteManyAsync(actor,
        [
            new PositionTemplateDeleteInput(first.Value!.Id, first.Value.Version),
            new PositionTemplateDeleteInput(second.Value!.Id, second.Value.Version + 1),
        ]);

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
        var listed = await service.ListAsync(actor, new PageRequest(1, 10));
        listed.Value!.Items.Should().HaveCount(2);
    }

    private static async Task<Guid> SeedDefinitionAsync(IAppDbContextFactory factory)
    {
        await using var db = factory.CreateDbContext();
        var definitionId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        db.AttributeCategories.Add(new AttributeCategory { Id = categoryId, Name = "Profile" });
        db.AttributeDefinitions.Add(new AttributeDefinition
        {
            Id = definitionId,
            CategoryId = categoryId,
            Name = "City",
            DataType = CvPlatform.Core.Enums.AttributeDataType.String,
        });
        await db.SaveChangesAsync();
        return definitionId;
    }
}
