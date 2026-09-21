using System.Text;
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
using CvPlatform.Infrastructure.Exports;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class ExportServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IAppDbContextFactory _factory;

    public ExportServiceTests()
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

    private ExportService Service(IProfileImageFetcher? imageFetcher = null)
    {
        var access = new PositionAccessService(_factory, new AccessRuleEngine());
        return new ExportService(
            new CvService(_factory, access), access,
            imageFetcher ?? new ProfileImageFetcher(new HttpClient()));
    }

    [Fact]
    public async Task ExportCvPdf_returns_pdf_and_respects_access()
    {
        var defId = Guid.NewGuid();
        await SeedDefinitionAsync(defId, "City", AttributeDataType.String);
        var skillId = Guid.NewGuid();
        await SeedDefinitionAsync(skillId, "Skill", AttributeDataType.String);
        var positionId = await SeedRestrictedPositionAsync(defId, [(skillId, false)]);
        var candidateId = await SeedCandidateAsync(
            ("City", AttributeDataType.String, "Warsaw", defId),
            ("Skill", AttributeDataType.String, "C#", skillId));
        var projectId = await SeedProjectAsync(candidateId, "Website");
        var cvId = await CreatePublishedCvAsync(candidateId, positionId, projectId);

        var pdf = await Service().ExportCvPdfAsync(
            new ActorContext(candidateId, false), cvId, "https://example.test");
        pdf.Succeeded.Should().BeTrue();
        pdf.Value![0..5].Should().BeEquivalentTo([(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-']);

        var strangerVisible = await Service().ExportCvPdfAsync(
            new ActorContext(Guid.NewGuid(), false), cvId, "https://example.test");
        strangerVisible.Succeeded.Should().BeTrue();

        await using (var db = _factory.CreateDbContext())
        {
            var profileId = await db.Profiles.Where(p => p.UserId == candidateId).Select(p => p.Id).SingleAsync();
            var value = await db.ProfileAttributeValues.SingleAsync(
                v => v.ProfileId == profileId && v.AttributeDefinitionId == defId);
            db.ProfileAttributeValues.Remove(value);
            await db.SaveChangesAsync();
        }

        var stranger = await Service().ExportCvPdfAsync(
            new ActorContext(Guid.NewGuid(), false), cvId, "https://example.test");
        stranger.Succeeded.Should().BeFalse();
        stranger.Error.Code.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ExportPositionsExcel_returns_workbook_with_rows()
    {
        var positionId = await SeedPositionAsync("Lead");
        var candidateId = await SeedCandidateAsync();
        await CreatePublishedCvAsync(candidateId, positionId);

        var excel = await Service().ExportPositionsExcelAsync(new ActorContext(Guid.NewGuid(), true));
        excel.Succeeded.Should().BeTrue();
        using var stream = new MemoryStream(excel.Value!);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Positions");
        sheet.Cell(1, 1).GetString().Should().Be("Title");
        sheet.RowsUsed().Count().Should().Be(2);
        sheet.Cell(2, 1).GetString().Should().Be("Lead");
    }

    [Fact]
    public async Task ExportPositionCvs_returns_dynamic_excel_and_csv_rows()
    {
        var cityId = Guid.NewGuid();
        var skillId = Guid.NewGuid();
        await SeedDefinitionAsync(cityId, "City", AttributeDataType.String);
        await SeedDefinitionAsync(skillId, "Skill", AttributeDataType.String);
        var positionId = await SeedRestrictedPositionAsync(cityId, [(skillId, false)]);
        var candidateId = await SeedCandidateAsync(
            ("City", AttributeDataType.String, "Warsaw", cityId),
            ("Skill", AttributeDataType.String, "=2+2", skillId));
        await CreatePublishedCvAsync(candidateId, positionId);

        var actor = new ActorContext(Guid.NewGuid(), true);
        var excel = await Service().ExportPositionCvsExcelAsync(actor, positionId);
        excel.Succeeded.Should().BeTrue();
        using (var stream = new MemoryStream(excel.Value!))
        using (var workbook = new XLWorkbook(stream))
        {
            var sheet = workbook.Worksheet("CVs");
            sheet.Cell(1, 1).GetString().Should().Be("CV ID");
            sheet.Row(1).CellsUsed().Select(cell => cell.GetString()).Should()
                .ContainInOrder("CV ID", "Candidate", "Status", "Published At", "City", "Skill", "Projects");
            sheet.Cell(2, 6).HasFormula.Should().BeFalse();
            sheet.Cell(2, 6).GetString().Should().Be("=2+2");
            sheet.RowsUsed().Count().Should().Be(2);
        }

        var csv = await Service().ExportPositionCvsCsvAsync(actor, positionId);
        csv.Succeeded.Should().BeTrue();
        var csvText = Encoding.UTF8.GetString(csv.Value!);
        csvText.Should().Contain("\"CV ID\"");
        csvText.Should().Contain("\"'=2+2\"");
        csvText.Should().Contain("\"Warsaw\"");
    }

    [Fact]
    public async Task ExportCvPdf_embeds_profile_photo_when_available()
    {
        var photoDefinitionId = Guid.NewGuid();
        await SeedDefinitionAsync(photoDefinitionId, "Me.Photo", AttributeDataType.Image);
        var positionId = await SeedPositionAsync("Engineer");
        var candidateId = await SeedCandidateAsync();
        await SeedImageValueAsync(candidateId, photoDefinitionId, "https://res.cloudinary.com/demo/image/upload/photo.png");
        await CreatePublishedCvAsync(candidateId, positionId);

        var fetcher = new FixedImageFetcher();
        var pdf = await Service(fetcher).ExportCvPdfAsync(
            new ActorContext(candidateId, false),
            await FindCvIdAsync(candidateId, positionId),
            "https://example.test");

        pdf.Succeeded.Should().BeTrue();
        fetcher.WasCalled.Should().BeTrue();
        pdf.Value![0..5].Should().BeEquivalentTo([(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-']);
    }

    private async Task<Guid> CreatePublishedCvAsync(Guid candidateId, Guid positionId, Guid? includeProjectId = null)
    {
        var access = new PositionAccessService(_factory, new AccessRuleEngine());
        var cvs = new CvService(_factory, access);
        var actor = new ActorContext(candidateId, false);
        var created = await cvs.CreateAsync(actor, positionId);
        created.Succeeded.Should().BeTrue();
        if (includeProjectId is { } projectId)
        {
            await using var db = _factory.CreateDbContext();
            var current = (await db.Cvs.SingleAsync(c => c.Id == created.Value!.Id)).Version;
            (await cvs.ToggleProjectAsync(actor, created.Value!.Id, new CvProjectToggleInput(projectId, current)))
                .Succeeded.Should().BeTrue();
        }
        await using var verify = _factory.CreateDbContext();
        var version = (await verify.Cvs.SingleAsync(c => c.Id == created.Value!.Id)).Version;
        (await cvs.PublishAsync(actor, created.Value!.Id, new CvStatusInput(version)))
            .Succeeded.Should().BeTrue();
        return created.Value!.Id;
    }

    private async Task<Guid> SeedProjectAsync(Guid userId, string name)
    {
        await using var db = _factory.CreateDbContext();
        var profileId = await db.Profiles.Where(p => p.UserId == userId).Select(p => p.Id).SingleAsync();
        var projectId = Guid.NewGuid();
        var tag = new ProjectTag { Id = Guid.NewGuid(), Name = "dotnet" };
        db.Projects.Add(new Project
        {
            Id = projectId,
            ProfileId = profileId,
            Name = name,
            DescriptionMarkdown = "**Built** a site",
            PeriodStart = new DateOnly(2023, 1, 1),
            PeriodEnd = new DateOnly(2023, 6, 1),
            Tags = [tag],
        });
        await db.SaveChangesAsync();
        return projectId;
    }

    private async Task<Guid> FindCvIdAsync(Guid candidateId, Guid positionId)
    {
        await using var db = _factory.CreateDbContext();
        return await db.Cvs.Where(cv => cv.Profile.UserId == candidateId && cv.PositionId == positionId)
            .Select(cv => cv.Id).SingleAsync();
    }

    private async Task SeedImageValueAsync(Guid userId, Guid definitionId, string url)
    {
        await using var db = _factory.CreateDbContext();
        var profileId = await db.Profiles.Where(profile => profile.UserId == userId)
            .Select(profile => profile.Id).SingleAsync();
        db.ProfileAttributeValues.Add(new ProfileAttributeValue
        {
            Id = Guid.NewGuid(),
            ProfileId = profileId,
            AttributeDefinitionId = definitionId,
            ImageUrl = url,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedPositionAsync(string title)
    {
        await using var db = _factory.CreateDbContext();
        var positionId = Guid.NewGuid();
        db.Positions.Add(new Position { Id = positionId, Title = title, IsPublic = true });
        await db.SaveChangesAsync();
        return positionId;
    }

    private async Task<Guid> SeedRestrictedPositionAsync(
        Guid definitionId,
        List<(Guid DefinitionId, bool IsRequired)>? extras = null)
    {
        await using var db = _factory.CreateDbContext();
        var positionId = Guid.NewGuid();
        var position = new Position
        {
            Id = positionId,
            Title = "Restricted",
            IsPublic = false,
            Attributes =
            [
                new PositionAttribute
                {
                    PositionId = positionId,
                    AttributeDefinitionId = definitionId,
                    IsRequired = true,
                    SortOrder = 0,
                },
            ],
            AccessRules =
            [
                new AccessRule
                {
                    Id = Guid.NewGuid(),
                    PositionId = positionId,
                    AttributeDefinitionId = definitionId,
                    DataType = AttributeDataType.String,
                    Operator = RuleOperator.Equals,
                    ComparisonValue = "Warsaw",
                },
            ],
        };
        foreach (var (extraId, required) in extras ?? [])
            position.Attributes.Add(new PositionAttribute
            {
                PositionId = positionId,
                AttributeDefinitionId = extraId,
                IsRequired = required,
                SortOrder = position.Attributes.Count,
            });
        db.Positions.Add(position);
        await db.SaveChangesAsync();
        return positionId;
    }

    private async Task SeedDefinitionAsync(Guid definitionId, string name, AttributeDataType type)
    {
        await using var db = _factory.CreateDbContext();
        db.AttributeCategories.Add(new AttributeCategory
        {
            Id = Guid.NewGuid(),
            Name = name,
            Definitions = [new AttributeDefinition { Id = definitionId, Name = name, DataType = type }],
        });
        await db.SaveChangesAsync();
    }

    private sealed class FixedImageFetcher : IProfileImageFetcher
    {
        private static readonly byte[] Image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

        public bool WasCalled { get; private set; }

        public Task<byte[]?> FetchAsync(string? imageUrl, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult<byte[]?>(Image);
        }
    }

    private async Task<Guid> SeedCandidateAsync()
    {
        await using var db = _factory.CreateDbContext();
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = $"c{userId:N}@example.test" });
        db.Profiles.Add(new Profile { Id = Guid.NewGuid(), UserId = userId });
        await db.SaveChangesAsync();
        return userId;
    }

    private async Task<Guid> SeedCandidateAsync(
        params (string Name, AttributeDataType Type, string? Value, Guid DefinitionId)[] values)
    {
        await using var db = _factory.CreateDbContext();
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = $"c{userId:N}@example.test" });
        db.Profiles.Add(new Profile { Id = profileId, UserId = userId });
        foreach (var (_, _, value, definitionId) in values)
            db.ProfileAttributeValues.Add(new ProfileAttributeValue
            {
                ProfileId = profileId,
                AttributeDefinitionId = definitionId,
                StringValue = value,
            });
        await db.SaveChangesAsync();
        return userId;
    }
}
