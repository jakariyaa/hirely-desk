using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CvPlatform.Infrastructure.Data;

public sealed class VersionIncrementInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyVersions(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        ApplyVersions(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    private static void ApplyVersions(DbContext? context)
    {
        if (context is null)
            return;

        foreach (var entry in context.ChangeTracker.Entries<IVersioned>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.Version = 1;
            else if (entry.State == EntityState.Modified && !IsSearchTextOnlyRefresh(entry))
                entry.Entity.Version = entry.OriginalValues.GetValue<long>(nameof(IVersioned.Version)) + 1;
        }
    }

    private static bool IsSearchTextOnlyRefresh(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<IVersioned> entry)
    {
        if (entry.Entity is not Cv)
            return false;
        var searchTextModified = false;
        foreach (var property in entry.Properties)
        {
            if (!property.IsModified)
                continue;
            if (property.Metadata.Name is nameof(IVersioned.Version))
                continue;
            if (property.Metadata.Name is nameof(Cv.SearchText))
            {
                searchTextModified = true;
                continue;
            }
            return false;
        }
        return searchTextModified;
    }
}
