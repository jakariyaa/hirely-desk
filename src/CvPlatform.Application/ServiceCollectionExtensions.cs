using CvPlatform.Application.Attributes;
using CvPlatform.Application.Profiles;
using CvPlatform.Application.Projects;
using CvPlatform.Application.Positions;
using CvPlatform.Application.Validation;
using CvPlatform.Core.Access;
using FluentValidation;
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
        services.AddTransient<PositionTemplates.IPositionTemplateService, PositionTemplates.PositionTemplateService>();
        services.AddTransient<CvPlatform.Application.Access.IPositionAccessService,
            CvPlatform.Application.Access.PositionAccessService>();
        services.AddTransient<CvPlatform.Application.Cvs.ICvService, CvPlatform.Application.Cvs.CvService>();
        services.AddTransient<CvPlatform.Application.Likes.ILikeService, CvPlatform.Application.Likes.LikeService>();
        services.AddTransient<CvPlatform.Application.Discussions.IDiscussionService, CvPlatform.Application.Discussions.DiscussionService>();
        services.AddTransient<CvPlatform.Application.Search.ISearchService, CvPlatform.Application.Search.SearchService>();
        services.AddTransient<CvPlatform.Application.Home.IHomeStatsService, CvPlatform.Application.Home.HomeStatsService>();
        services.AddTransient<CvPlatform.Application.Badges.IBadgeService, CvPlatform.Application.Badges.BadgeService>();
        services.AddSingleton<CvPlatform.Application.Discussions.IDiscussionNotifier, CvPlatform.Application.Discussions.DiscussionNotifier>();
        services.AddSingleton<IAccessRuleEngine, AccessRuleEngine>();
        services.AddTransient<IValidator<AttributeDefinitionInput>, AttributeDefinitionInputValidator>();
        services.AddTransient<IValidator<PositionInput>, PositionInputValidator>();
        services.AddTransient<IValidator<ProjectInput>, ProjectInputValidator>();
        services.AddTransient<IValidator<CvPlatform.Application.Discussions.DiscussionPostInput>, DiscussionPostInputValidator>();
        return services;
    }
}
