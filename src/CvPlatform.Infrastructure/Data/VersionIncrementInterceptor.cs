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
            else if (entry.State == EntityState.Modified)
                entry.Entity.Version = entry.OriginalValues.GetValue<long>(nameof(IVersioned.Version)) + 1;
        }
    }
}
