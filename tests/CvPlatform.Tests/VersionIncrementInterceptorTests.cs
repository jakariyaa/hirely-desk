using CvPlatform.Core.Entities;
using CvPlatform.Infrastructure.Data;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CvPlatform.Tests;

public class VersionIncrementInterceptorTests
{
    private sealed class FakeEntity : IVersioned
    {
        public Guid Id { get; set; }
        public long Version { get; set; }
    }

    private sealed class FakeDbContext(DbContextOptions<FakeDbContext> options) : DbContext(options)
    {
        public DbSet<FakeEntity> Entities => Set<FakeEntity>();
    }

    private static FakeDbContext CreateDb() => new(new DbContextOptionsBuilder<FakeDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static Task InterceptAsync(DbContext context) =>
        new VersionIncrementInterceptor()
            .SavingChangesAsync(new DbContextEventData(null!, null!, context), new InterceptionResult<int>())
            .AsTask();

    [Fact]
    public async Task Added_entry_yields_version_1()
    {
        using var db = CreateDb();
        var entity = new FakeEntity { Id = Guid.NewGuid() };
        db.Entities.Add(entity);

        await InterceptAsync(db);

        entity.Version.Should().Be(1);
    }

    [Fact]
    public async Task Modified_entry_increments_original_version()
    {
        using var db = CreateDb();
        var entity = new FakeEntity { Id = Guid.NewGuid(), Version = 7 };
        db.Entities.Attach(entity);
        db.Entry(entity).State = EntityState.Modified;

        await InterceptAsync(db);

        entity.Version.Should().Be(8);
    }

    [Fact]
    public async Task Unchanged_entries_are_left_alone()
    {
        using var db = CreateDb();
        var kept = new FakeEntity { Id = Guid.NewGuid(), Version = 3 };
        var changed = new FakeEntity { Id = Guid.NewGuid(), Version = 4 };
        db.Entities.AttachRange(kept, changed);
        db.Entry(changed).State = EntityState.Modified;

        await InterceptAsync(db);

        kept.Version.Should().Be(3);
        changed.Version.Should().Be(5);
    }
}
