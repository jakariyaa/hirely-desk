using AwesomeAssertions;
using CvPlatform.Web.Components.Pages;
using CvPlatform.Web.Components.Shared;
using CvPlatform.Application.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;

namespace CvPlatform.Tests;

public class RoutingTests
{
    [Fact]
    public void RouteTemplates_are_unique_case_insensitively()
    {
        var assembly = typeof(PageHeader).Assembly;
        var routes = assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(RouteAttribute), inherit: false)
                .Cast<RouteAttribute>()
                .Select(a => new { Type = t.FullName, a.Template }))
            .ToList();

        routes.Should().NotBeEmpty();
        var duplicates = routes
            .GroupBy(r => r.Template, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        duplicates.Should().BeEmpty("route templates must not collide case-insensitively");
    }

    [Fact]
    public void Positions_browse_route_is_public()
    {
        typeof(Positions).GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true)
            .Should().ContainSingle();
    }

    [Fact]
    public void Attribute_catalog_route_is_restricted_to_admins_and_recruiters()
    {
        var authorization = typeof(AttributeCatalog)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        authorization.Roles.Should().Be("Admin,Recruiter");
    }

    [Fact]
    public void Candidate_role_is_explicitly_defined()
    {
        Roles.Candidate.Should().Be("Candidate");
    }
}
