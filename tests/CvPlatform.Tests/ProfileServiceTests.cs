using CvPlatform.Application.Attributes;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Profiles;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using CvPlatform.Infrastructure.Data;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class ProfileServiceTests
{
    private static ActorContext Actor(Guid userId) => new(userId, false);
    private sealed class TestFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
    {
        public IAppDbContext CreateDbContext() => factory.CreateDbContext();
    }

    private static IAppDbContextFactory CreateFactory(out AppDbContext db)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new VersionIncrementInterceptor()));
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        db = factory.CreateDbContext();
        return new TestFactory(factory);
    }

    private static async Task<AttributeDefinition> SeedDefinitionAsync(
        AppDbContext db, string name, AttributeDataType type, string? options = null)
    {
        var category = await db.AttributeCategories.SingleOrDefaultAsync(c => c.Name == "Me");
        if (category is null)
        {
            category = new AttributeCategory { Id = Guid.NewGuid(), Name = "Me" };
            db.AttributeCategories.Add(category);
            await db.SaveChangesAsync();
        }

        var definition = new AttributeDefinition
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Name = name,
            DataType = type,
            IsBuiltIn = true,
            OptionsJson = options,
        };
        db.AttributeDefinitions.Add(definition);
        await db.SaveChangesAsync();
        return definition;
    }

    [Fact]
    public async Task GetForUserAsync_creates_profile_on_first_access()
    {
        var factory = CreateFactory(out var db);
        var service = new ProfileService(factory);
        var userId = Guid.NewGuid();

        var result = await service.GetForUserAsync(Actor(userId), userId);

        result.Succeeded.Should().BeTrue();
        db.Profiles.Should().ContainSingle(p => p.UserId == userId);
    }

    [Fact]
    public async Task SaveAttributeValueAsync_creates_then_updates_value()
    {
        var factory = CreateFactory(out var db);
        var service = new ProfileService(factory);
        var userId = Guid.NewGuid();
        var definition = await SeedDefinitionAsync(db, "Me.Phone", AttributeDataType.String);

        var create = await service.SaveAttributeValueAsync(
            Actor(userId), userId, new AttributeValueInput(definition.Id, StringValue: "  123-456  "));
        create.Succeeded.Should().BeTrue();

        db.ProfileAttributeValues.Should().ContainSingle(v =>
            v.StringValue == "123-456" && v.AttributeDefinitionId == definition.Id);

        var update = await service.SaveAttributeValueAsync(
            Actor(userId), userId, new AttributeValueInput(
                definition.Id, StringValue: "987", ExpectedVersion: create.Value!.Values.Single().Version));
        update.Succeeded.Should().BeTrue();
        db.ChangeTracker.Clear();
        db.ProfileAttributeValues.Should().ContainSingle(v => v.StringValue == "987");
    }

    [Fact]
    public async Task SaveAttributeValueAsync_rejects_dropdown_option_outside_choices()
    {
        var factory = CreateFactory(out var db);
        var service = new ProfileService(factory);
        var definition = await SeedDefinitionAsync(
            db, "IELTS Band", AttributeDataType.Dropdown, """{"choices":["6","7","8"]}""");

        var result = await service.SaveAttributeValueAsync(
            Actor(Guid.Empty), Guid.Empty, new AttributeValueInput(definition.Id, DropdownOption: "9"));

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task SaveAttributeValueAsync_accepts_dropdown_option_within_choices()
    {
        var factory = CreateFactory(out var db);
        var service = new ProfileService(factory);
        var definition = await SeedDefinitionAsync(
            db, "IELTS Band", AttributeDataType.Dropdown, """{"choices":["6","7","8"]}""");

        var result = await service.SaveAttributeValueAsync(
            Actor(Guid.Empty), Guid.Empty, new AttributeValueInput(definition.Id, DropdownOption: "7"));

        result.Succeeded.Should().BeTrue();
        db.ProfileAttributeValues.Should().ContainSingle(v => v.DropdownOption == "7");
    }

    [Fact]
    public async Task SaveAttributeValueAsync_unknown_definition_fails()
    {
        var factory = CreateFactory(out var db);
        var service = new ProfileService(factory);

        var result = await service.SaveAttributeValueAsync(
            Actor(Guid.Empty), Guid.Empty, new AttributeValueInput(Guid.NewGuid(), StringValue: "x"));

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task SaveAttributeValueAsync_rejects_stale_version()
    {
        var factory = CreateFactory(out var db);
        var service = new ProfileService(factory);
        var userId = Guid.NewGuid();
        var definition = await SeedDefinitionAsync(db, "Me.Phone", AttributeDataType.String);

        var created = await service.SaveAttributeValueAsync(
            Actor(userId), userId, new AttributeValueInput(definition.Id, StringValue: "123"));

        var stale = await service.SaveAttributeValueAsync(
            Actor(userId), userId, new AttributeValueInput(definition.Id, StringValue: "456", ExpectedVersion: 0));

        created.Succeeded.Should().BeTrue();
        stale.Succeeded.Should().BeFalse();
        stale.Error.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
        db.ProfileAttributeValues.Single().StringValue.Should().Be("123");
    }

    [Fact]
    public async Task GetForUserAsync_rejects_different_actor()
    {
        var factory = CreateFactory(out _);
        var service = new ProfileService(factory);

        var result = await service.GetForUserAsync(
            Actor(Guid.NewGuid()), Guid.NewGuid());

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task SaveAttributeValueAsync_rejects_value_for_wrong_data_type()
    {
        var factory = CreateFactory(out var db);
        var service = new ProfileService(factory);
        var definition = await SeedDefinitionAsync(db, "Me.BirthDate", AttributeDataType.Date);

        var result = await service.SaveAttributeValueAsync(
            Actor(Guid.Empty), Guid.Empty,
            new AttributeValueInput(definition.Id, StringValue: "not-a-date"));

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task SaveAttributeValueAsync_enforces_numeric_options()
    {
        var factory = CreateFactory(out var db);
        var service = new ProfileService(factory);
        var definition = await SeedDefinitionAsync(
            db, "Me.Score", AttributeDataType.Numeric, """{"min":1,"max":10}""");

        var result = await service.SaveAttributeValueAsync(
            Actor(Guid.Empty), Guid.Empty,
            new AttributeValueInput(definition.Id, NumericValue: 11));

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.ValidationFailed);
    }
}
