using CvPlatform.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Infrastructure.Data;

public sealed class AppDbContextFactoryAdapter(IDbContextFactory<AppDbContext> inner) : IAppDbContextFactory
{
    public IAppDbContext CreateDbContext() => inner.CreateDbContext();
}
