using CvPlatform.Application.Attributes;
using CvPlatform.Application.Profiles;
using CvPlatform.Application.Projects;
using CvPlatform.Application.Positions;
using CvPlatform.Core.Access;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCvPlatformApplication(this IServiceCollection services)
    {
        services.AddTransient<IAttributeCatalog, AttributeCatalogService>();
        services.AddTransient<IAttributeDefinitionService, AttributeDefinitionService>();
        services.AddTransient<IProfileService, ProfileService>();
        services.AddTransient<IProjectService, ProjectService>();
        services.AddTransient<IPositionService, PositionService>();
        services.AddSingleton<IAccessRuleEngine, AccessRuleEngine>();
        return services;
    }
}
