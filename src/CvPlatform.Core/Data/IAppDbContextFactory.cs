namespace CvPlatform.Core.Data;

public interface IAppDbContextFactory
{
    IAppDbContext CreateDbContext();
}
