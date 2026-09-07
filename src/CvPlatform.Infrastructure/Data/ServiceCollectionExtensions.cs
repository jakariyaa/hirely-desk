using CvPlatform.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Infrastructure.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCvPlatformDatabase(this IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<AppDbContext>(o => o
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new VersionIncrementInterceptor()));
        services.AddSingleton<IAppDbContextFactory>(sp =>
            new AppDbContextFactoryAdapter(sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));
        return services;
    }
}
