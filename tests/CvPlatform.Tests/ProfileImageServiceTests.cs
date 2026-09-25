using AwesomeAssertions;
using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Profiles;
using CvPlatform.Core.Access;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using CvPlatform.Core.Storage;
using CvPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class ProfileImageServiceTests
{
    private sealed class TestImageStorage : IImageStorage
    {
        public bool IsConfigured => true;
        public long MaxUploadBytes => 5 * 1024 * 1024;

        public bool IsAllowedContentType(string contentType) => true;

        public ImageUploadTicket? CreateUploadTicket(Guid userId, string contentType, long size) => null;

        public ImageDownloadTicket? CreateDownloadTicket(string objectKey) =>
            new($"https://s3.example.test/get/{Uri.EscapeDataString(objectKey)}", DateTimeOffset.UtcNow.AddMinutes(1));

        public Task<string?> CompleteUploadAsync(
            Guid userId,
            string objectKey,
            string contentType,
            long size,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(objectKey);

        public Task<StoredImage?> OpenObjectAsync(
            string objectKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<StoredImage?>(null);

        public bool IsOwnedObjectKey(string objectKey, Guid userId) =>
            objectKey.StartsWith($"users/{userId:D}/profile/", StringComparison.Ordinal);
    }

    private sealed class TestFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
    {
        public IAppDbContext CreateDbContext() => factory.CreateDbContext();
    }

    private static (IAppDbContextFactory Factory, AppDbContext Db) CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new VersionIncrementInterceptor()));
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return (new TestFactory(factory), factory.CreateDbContext());
    }

    [Fact]
    public async Task Owner_can_obtain_a_short_lived_download_ticket()
    {
        var (factory, db) = CreateFactory();
        var userId = Guid.NewGuid();
        var profile = new Profile { Id = Guid.NewGuid(), UserId = userId };
        var category = new AttributeCategory { Id = Guid.NewGuid(), Name = "Me" };
        var definition = new AttributeDefinition
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Name = ProfileAttributeNames.Photo,
            DataType = AttributeDataType.Image,
            IsBuiltIn = true,
        };
        var value = new ProfileAttributeValue
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            AttributeDefinitionId = definition.Id,
            ImageObjectKey = $"users/{userId:D}/profile/{Guid.NewGuid():N}.png",
        };
        db.AddRange(profile, category, definition, value);
        await db.SaveChangesAsync();

        var service = new ProfileImageService(
            factory,
            new PositionAccessService(factory, new AccessRuleEngine()),
            new TestImageStorage());

        var result = await service.CreateDownloadTicketAsync(new ActorContext(userId, false), value.Id);

        result.Succeeded.Should().BeTrue();
        result.Value!.DownloadUrl.Should().Contain("users%2F");
    }

    [Fact]
    public async Task Different_user_cannot_obtain_a_download_ticket_without_cv_access()
    {
        var (factory, db) = CreateFactory();
        var ownerId = Guid.NewGuid();
        var profile = new Profile { Id = Guid.NewGuid(), UserId = ownerId };
        var category = new AttributeCategory { Id = Guid.NewGuid(), Name = "Me" };
        var definition = new AttributeDefinition
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Name = ProfileAttributeNames.Photo,
            DataType = AttributeDataType.Image,
            IsBuiltIn = true,
        };
        var value = new ProfileAttributeValue
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            AttributeDefinitionId = definition.Id,
            ImageObjectKey = $"users/{ownerId:D}/profile/{Guid.NewGuid():N}.png",
        };
        db.AddRange(profile, category, definition, value);
        await db.SaveChangesAsync();

        var service = new ProfileImageService(
            factory,
            new PositionAccessService(factory, new AccessRuleEngine()),
            new TestImageStorage());

        var result = await service.CreateDownloadTicketAsync(
            new ActorContext(Guid.NewGuid(), false), value.Id);

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Viewer_with_cv_access_can_obtain_a_download_ticket_for_a_published_cv()
    {
        var (factory, db) = CreateFactory();
        var ownerId = Guid.NewGuid();
        var profile = new Profile { Id = Guid.NewGuid(), UserId = ownerId };
        var category = new AttributeCategory { Id = Guid.NewGuid(), Name = "Me" };
        var definition = new AttributeDefinition
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Name = ProfileAttributeNames.Photo,
            DataType = AttributeDataType.Image,
            IsBuiltIn = true,
        };
        var value = new ProfileAttributeValue
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            AttributeDefinitionId = definition.Id,
            ImageObjectKey = $"users/{ownerId:D}/profile/{Guid.NewGuid():N}.png",
        };
        var position = new Position
        {
            Id = Guid.NewGuid(),
            Title = "Public position",
            IsPublic = true,
        };
        var cv = new Cv
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            PositionId = position.Id,
            CreatedAt = DateTime.UtcNow,
            Status = CvStatus.Published,
            PublishedAt = DateTime.UtcNow,
        };
        db.AddRange(profile, category, definition, value, position, cv);
        await db.SaveChangesAsync();

        var service = new ProfileImageService(
            factory,
            new PositionAccessService(factory, new AccessRuleEngine()),
            new TestImageStorage());

        var result = await service.CreateDownloadTicketAsync(
            new ActorContext(Guid.NewGuid(), false, true), value.Id, cv.Id);

        result.Succeeded.Should().BeTrue();
    }
}
