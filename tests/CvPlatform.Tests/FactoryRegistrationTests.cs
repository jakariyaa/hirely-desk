using CvPlatform.Infrastructure.Data;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class FactoryRegistrationTests
{
    [Fact]
    public async Task Factory_options_carry_the_version_interceptor()
    {
        var services = new ServiceCollection();
        services.AddCvPlatformDatabase("Host=localhost;Database=cvplatform_probe");
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = factory.CreateDbContext();
        var options = ((IInfrastructure<IServiceProvider>)db).Instance.GetRequiredService<IDbContextOptions>();
        var interceptors = options.Extensions.OfType<CoreOptionsExtension>().Single().Interceptors;
        interceptors.Should().NotBeNull();
        interceptors!.OfType<VersionIncrementInterceptor>().Should().ContainSingle();
    }
}
