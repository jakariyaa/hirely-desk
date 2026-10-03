using AwesomeAssertions;
using CvPlatform.Application.Common;
using CvPlatform.Application.Integration;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using CvPlatform.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class PositionSummaryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IAppDbContextFactory _factory;
    private readonly PositionSummaryService _service;
    private readonly FixedTimeProvider _time = new(
        new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _positionId = Guid.NewGuid();
    private readonly Guid _otherPositionId = Guid.NewGuid();

    public PositionSummaryServiceTests()
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
        _service = new PositionSummaryService(_factory, _time);
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TestFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
    {
        public IAppDbContext CreateDbContext() => factory.CreateDbContext();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record Value(Attr Attribute, string? String = null, string? Text = null,
        decimal? Numeric = null, DateOnly? Date = null, DateOnly? PeriodStart = null,
        DateOnly? PeriodEnd = null, bool? Boolean = null, string? Dropdown = null, string? Image = null);

    [Fact]
    public async Task Numeric_attributes_aggregate_count_average_min_and_max_in_sql()
    {
        var experience = await SeedDefinitionAsync("Experience", AttributeDataType.Numeric);
        var expected = await SeedDefinitionAsync("Expected", AttributeDataType.Numeric);
        await using (var db = _factory.CreateDbContext())
        {
            await SeedPositionAsync(db, _positionId, [(experience, true, 0), (expected, false, 1)]);
            await SeedCandidateWithCvAsync(db, _positionId, [new Value(experience, Numeric: 2m)]);
            await SeedCandidateWithCvAsync(db, _positionId, [new Value(experience, Numeric: 8m)]);
            await SeedCandidateWithCvAsync(db, _positionId, [new Value(experience, Numeric: 4m)]);
            await SeedCandidateWithoutCvAsync(db, [new Value(experience, Numeric: 100m)]);
        }

        var result = await _service.GetSummaryAsync(_positionId);

        result.Succeeded.Should().BeTrue();
        var summary = result.Value!;
        summary.CvCount.Should().Be(3);
        summary.Title.Should().Be("Senior .NET Developer");
        summary.Company.Should().Be("Demo Corp");
        summary.GeneratedAt.Should().Be(new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc));

        var numeric = summary.Attributes.Single(a => a.Name == "Experience");
        numeric.DataType.Should().Be(AttributeDataType.Numeric);
        numeric.FilledCount.Should().Be(3);
        numeric.Numeric!.Count.Should().Be(3);
        numeric.Numeric.Average.Should().Be(14m / 3m);
        numeric.Numeric.Min.Should().Be(2m);
        numeric.Numeric.Max.Should().Be(8m);

        var empty = summary.Attributes.Single(a => a.Name == "Expected");
        empty.FilledCount.Should().Be(0);
        empty.Numeric!.Count.Should().Be(0);
        empty.Numeric.Average.Should().BeNull();
        empty.Numeric.Min.Should().BeNull();
        empty.Numeric.Max.Should().BeNull();
    }

    [Fact]
    public async Task Text_and_dropdown_attributes_return_top_values_ranked_by_frequency()
    {
        var city = await SeedDefinitionAsync("City", AttributeDataType.String);
        var about = await SeedDefinitionAsync("About", AttributeDataType.Text);
        var ielts = await SeedDefinitionAsync("IELTS", AttributeDataType.Dropdown);
        await using (var db = _factory.CreateDbContext())
        {
            await SeedPositionAsync(db, _positionId, [(city, true, 0), (about, false, 1), (ielts, false, 2)]);
            await SeedCandidateWithCvAsync(db, _positionId,
                [new Value(city, String: "Warsaw"), new Value(about, Text: "Backend developer"), new Value(ielts, Dropdown: "7")]);
            await SeedCandidateWithCvAsync(db, _positionId,
                [new Value(city, String: "Warsaw"), new Value(about, Text: "Frontend developer"), new Value(ielts, Dropdown: "6.5")]);
            await SeedCandidateWithCvAsync(db, _positionId,
                [new Value(city, String: "Krakow"), new Value(about, Text: "Backend developer")]);
            await SeedCandidateWithCvAsync(db, _positionId,
                [new Value(city, String: "Warsaw"), new Value(about, Text: "Backend developer")]);
        }

        var summary = (await _service.GetSummaryAsync(_positionId)).Value!;

        var citySummary = summary.Attributes.Single(a => a.Name == "City");
        citySummary.FilledCount.Should().Be(4);
        citySummary.DistinctValueCount.Should().Be(2);
        citySummary.TopValues.Should().HaveCount(2);
        citySummary.TopValues![0].Should().Be(new TopValueDto("Warsaw", 3));
        citySummary.TopValues[1].Should().Be(new TopValueDto("Krakow", 1));

        var aboutSummary = summary.Attributes.Single(a => a.Name == "About");
        aboutSummary.FilledCount.Should().Be(4);
        aboutSummary.DistinctValueCount.Should().Be(2);
        aboutSummary.TopValues![0].Should().Be(new TopValueDto("Backend developer", 3));
        aboutSummary.TopValues[1].Should().Be(new TopValueDto("Frontend developer", 1));

        var ieltsSummary = summary.Attributes.Single(a => a.Name == "IELTS");
        ieltsSummary.FilledCount.Should().Be(2);
        ieltsSummary.TopValues!.Should().HaveCount(2);
    }

    [Fact]
    public async Task Top_values_are_limited_to_five_per_attribute()
    {
        var city = await SeedDefinitionAsync("City", AttributeDataType.String);
        await using (var db = _factory.CreateDbContext())
        {
            await SeedPositionAsync(db, _positionId, [(city, false, 0)]);
            for (var i = 0; i < 7; i++)
                await SeedCandidateWithCvAsync(db, _positionId, [new Value(city, String: $"City-{i}")]);
        }

        var summary = (await _service.GetSummaryAsync(_positionId)).Value!;

        var citySummary = summary.Attributes.Single(a => a.Name == "City");
        citySummary.FilledCount.Should().Be(7);
        citySummary.DistinctValueCount.Should().Be(7);
        citySummary.TopValues!.Should().HaveCount(5);
    }

    [Fact]
    public async Task Boolean_date_period_and_image_attributes_use_type_specific_aggregates()
    {
        var remote = await SeedDefinitionAsync("Remote", AttributeDataType.Boolean);
        var birth = await SeedDefinitionAsync("BirthDate", AttributeDataType.Date);
        var worked = await SeedDefinitionAsync("Worked", AttributeDataType.Period);
        var photo = await SeedDefinitionAsync("Photo", AttributeDataType.Image);
        await using (var db = _factory.CreateDbContext())
        {
            await SeedPositionAsync(db, _positionId,
                [(remote, false, 0), (birth, false, 1), (worked, false, 2), (photo, false, 3)]);
            await SeedCandidateWithCvAsync(db, _positionId, [
                new Value(remote, Boolean: true),
                new Value(birth, Date: new DateOnly(1995, 5, 1)),
                new Value(worked, PeriodStart: new DateOnly(2020, 1, 1), PeriodEnd: new DateOnly(2022, 6, 30)),
                new Value(photo, Image: "users/photo-1.png"),
            ]);
            await SeedCandidateWithCvAsync(db, _positionId, [
                new Value(remote, Boolean: false),
                new Value(birth, Date: new DateOnly(1990, 1, 15)),
                new Value(worked, PeriodStart: new DateOnly(2018, 3, 1), PeriodEnd: new DateOnly(2021, 12, 31)),
                new Value(photo, Image: "users/photo-2.png"),
            ]);
            await SeedCandidateWithoutCvAsync(db, [new Value(remote, Boolean: true)]);
        }

        var summary = (await _service.GetSummaryAsync(_positionId)).Value!;

        var remoteSummary = summary.Attributes.Single(a => a.Name == "Remote");
        remoteSummary.FilledCount.Should().Be(2);
        remoteSummary.Boolean.Should().Be(new BooleanAggregateDto(1, 1));

        var birthSummary = summary.Attributes.Single(a => a.Name == "BirthDate");
        birthSummary.FilledCount.Should().Be(2);
        birthSummary.Date.Should().Be(new DateAggregateDto(2,
            new DateOnly(1990, 1, 15), new DateOnly(1995, 5, 1)));

        var workedSummary = summary.Attributes.Single(a => a.Name == "Worked");
        workedSummary.FilledCount.Should().Be(2);
        workedSummary.Period.Should().Be(new PeriodAggregateDto(2,
            new DateOnly(2018, 3, 1), new DateOnly(2022, 6, 30)));

        var photoSummary = summary.Attributes.Single(a => a.Name == "Photo");
        photoSummary.FilledCount.Should().Be(2);
        photoSummary.TopValues.Should().BeNull();
        photoSummary.Numeric.Should().BeNull();
    }

    [Fact]
    public async Task Attributes_without_values_have_a_stable_contract()
    {
        var city = await SeedDefinitionAsync("City", AttributeDataType.String);
        var years = await SeedDefinitionAsync("Years", AttributeDataType.Numeric);
        var remote = await SeedDefinitionAsync("Remote", AttributeDataType.Boolean);
        await using (var db = _factory.CreateDbContext())
        {
            await SeedPositionAsync(db, _positionId, [(city, false, 0), (years, false, 1), (remote, false, 2)]);
            await SeedEmptyCandidateWithCvAsync(db, _positionId);
        }

        var summary = (await _service.GetSummaryAsync(_positionId)).Value!;

        summary.CvCount.Should().Be(1);
        var citySummary = summary.Attributes.Single(a => a.Name == "City");
        citySummary.FilledCount.Should().Be(0);
        citySummary.TopValues.Should().BeEmpty();
        citySummary.DistinctValueCount.Should().Be(0);

        var yearsSummary = summary.Attributes.Single(a => a.Name == "Years");
        yearsSummary.FilledCount.Should().Be(0);
        yearsSummary.Numeric!.Count.Should().Be(0);
        yearsSummary.Numeric.Average.Should().BeNull();

        var remoteSummary = summary.Attributes.Single(a => a.Name == "Remote");
        remoteSummary.FilledCount.Should().Be(0);
        remoteSummary.Boolean.Should().Be(new BooleanAggregateDto(0, 0));
    }

    [Fact]
    public async Task Data_from_other_positions_never_leaks_into_the_summary()
    {
        var city = await SeedDefinitionAsync("City", AttributeDataType.String);
        await using (var db = _factory.CreateDbContext())
        {
            await SeedPositionAsync(db, _positionId, [(city, false, 0)]);
            await SeedPositionAsync(db, _otherPositionId, [(city, false, 0)]);
            await SeedCandidateWithCvAsync(db, _positionId, [new Value(city, String: "Warsaw")]);
            await SeedCandidateWithCvAsync(db, _otherPositionId, [new Value(city, String: "Paris")]);
            await SeedCandidateWithCvAsync(db, _otherPositionId, [new Value(city, String: "Paris")]);
        }

        var summary = (await _service.GetSummaryAsync(_positionId)).Value!;

        summary.CvCount.Should().Be(1);
        var citySummary = summary.Attributes.Single(a => a.Name == "City");
        citySummary.FilledCount.Should().Be(1);
        citySummary.TopValues!.Should().ContainSingle().Which.Should().Be(new TopValueDto("Warsaw", 1));
    }

    [Fact]
    public async Task Missing_position_is_reported_as_not_found()
    {
        var result = await _service.GetSummaryAsync(Guid.NewGuid());

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Last_used_is_stamped_once_per_throttle_window()
    {
        var city = await SeedDefinitionAsync("City", AttributeDataType.String);
        Guid tokenId;
        await using (var db = _factory.CreateDbContext())
        {
            await SeedPositionAsync(db, _positionId, [(city, false, 0)]);
            var token = new PositionApiToken
            {
                Id = Guid.NewGuid(),
                PositionId = _positionId,
                Name = "test",
                TokenHash = PositionApiTokenSecret.Hash("cvp_test"),
                CreatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            };
            db.PositionApiTokens.Add(token);
            await db.SaveChangesAsync();
            tokenId = token.Id;
        }

        (await _service.GetSummaryAsync(_positionId, tokenId)).Succeeded.Should().BeTrue();
        await using (var db = _factory.CreateDbContext())
        {
            var stamp = await db.PositionApiTokens.SingleAsync(t => t.Id == tokenId);
            stamp.LastUsedAt.Should().Be(new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc));
        }

        (await _service.GetSummaryAsync(_positionId, tokenId)).Succeeded.Should().BeTrue();
        await using (var db = _factory.CreateDbContext())
        {
            var stamp = await db.PositionApiTokens.SingleAsync(t => t.Id == tokenId);
            stamp.LastUsedAt.Should().Be(new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc));
        }

        _time.Now = _time.Now.AddMinutes(2);
        (await _service.GetSummaryAsync(_positionId, tokenId)).Succeeded.Should().BeTrue();
        await using (var db = _factory.CreateDbContext())
        {
            var stamp = await db.PositionApiTokens.SingleAsync(t => t.Id == tokenId);
            stamp.LastUsedAt.Should().Be(new DateTime(2026, 10, 5, 12, 2, 0, DateTimeKind.Utc));
        }
    }

    private async Task<Attr> SeedDefinitionAsync(string name, AttributeDataType dataType)
    {
        await using var db = _factory.CreateDbContext();
        var categoryId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        db.AttributeCategories.Add(new AttributeCategory { Id = categoryId, Name = $"Category-{definitionId:N}" });
        db.AttributeDefinitions.Add(new AttributeDefinition
        {
            Id = definitionId,
            CategoryId = categoryId,
            Name = name,
            DataType = dataType,
        });
        await db.SaveChangesAsync();
        return new Attr(name, definitionId);
    }

    private static async Task SeedPositionAsync(
        IAppDbContext db, Guid positionId, (Attr Attribute, bool Required, int Sort)[] attributes)
    {
        db.Positions.Add(new Position
        {
            Id = positionId,
            Title = "Senior .NET Developer",
            Company = "Demo Corp",
            Level = "Senior",
            ShortDescription = "Demo position",
            IsPublic = true,
            Attributes = attributes.Select(a => new PositionAttribute
            {
                PositionId = positionId,
                AttributeDefinitionId = a.Attribute.DefinitionId,
                IsRequired = a.Required,
                SortOrder = a.Sort,
            }).ToList(),
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedCandidateWithCvAsync(
        IAppDbContext db, Guid positionId, Value[] values)
    {
        var profile = NewProfile();
        db.Profiles.Add(profile);
        foreach (var value in values)
            db.ProfileAttributeValues.Add(ToValue(profile.Id, value));
        db.Cvs.Add(new Cv
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            PositionId = positionId,
            Status = CvStatus.Draft,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedEmptyCandidateWithCvAsync(IAppDbContext db, Guid positionId)
    {
        var profile = NewProfile();
        db.Profiles.Add(profile);
        db.Cvs.Add(new Cv
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            PositionId = positionId,
            Status = CvStatus.Draft,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedCandidateWithoutCvAsync(IAppDbContext db, Value[] values)
    {
        var profile = NewProfile();
        db.Profiles.Add(profile);
        foreach (var value in values)
            db.ProfileAttributeValues.Add(ToValue(profile.Id, value));
        await db.SaveChangesAsync();
    }

    private static Profile NewProfile()
    {
        var userId = Guid.NewGuid();
        return new Profile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            User = new ApplicationUser { Id = userId, UserName = $"u{userId:N}@example.test" },
        };
    }

    private static ProfileAttributeValue ToValue(Guid profileId, Value value) => new()
    {
        Id = Guid.NewGuid(),
        ProfileId = profileId,
        AttributeDefinitionId = value.Attribute.DefinitionId,
        StringValue = value.String,
        TextValue = value.Text,
        NumericValue = value.Numeric,
        DateValue = value.Date,
        PeriodStart = value.PeriodStart,
        PeriodEnd = value.PeriodEnd,
        BooleanValue = value.Boolean,
        DropdownOption = value.Dropdown,
        ImageObjectKey = value.Image,
    };

    private sealed record Attr(string Name, Guid DefinitionId);
}
