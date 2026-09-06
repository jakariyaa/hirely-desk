using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Web.Seed;

public static class SeedData
{
    public const string AdminEmail = "admin@cvplatform.local";
    public const string AdminRole = "Admin";
    public const string DemoCandidateEmail = "candidate@cvplatform.local";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(AdminRole))
            await roles.CreateAsync(new IdentityRole<Guid>(AdminRole));

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var factory = sp.GetRequiredService<IAppDbContextFactory>();

        await EnsureUserAsync(users, factory, AdminEmail, config["Seed:AdminPassword"], [AdminRole]);
        await EnsureUserAsync(users, factory, DemoCandidateEmail, config["Seed:DemoPassword"], []);

        await using var db = factory.CreateDbContext();
        await EnsureCategoriesAsync(db);
        await EnsureAttributesAsync(db);
        await EnsureDemoPositionAsync(db);
        await db.SaveChangesAsync();
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> users,
        IAppDbContextFactory factory,
        string email,
        string? password,
        string[] roles)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException(
                    $"Missing seed password for {email}. Set it with: dotnet user-secrets set \"Seed:AdminPassword\" \"...\"");
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
                throw new InvalidOperationException(
                    $"Seed user {email} failed: {string.Join("; ", created.Errors.Select(e => e.Description))}");
        }

        foreach (var role in roles)
            if (!await users.IsInRoleAsync(user, role))
                await users.AddToRoleAsync(user, role);

        await using var db = factory.CreateDbContext();
        if (!await db.Profiles.AnyAsync(p => p.UserId == user.Id))
        {
            db.Profiles.Add(new Profile { Id = Guid.NewGuid(), UserId = user.Id });
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureCategoriesAsync(IAppDbContext db)
    {
        foreach (var name in (string[])["Me", "Education", "Experience", "Skills", "Languages"])
            if (!await db.AttributeCategories.AnyAsync(c => c.Name == name))
                db.AttributeCategories.Add(new AttributeCategory { Id = Guid.NewGuid(), Name = name });
        await db.SaveChangesAsync();
    }

    private static async Task EnsureAttributesAsync(IAppDbContext db)
    {
        var categories = await db.AttributeCategories.ToDictionaryAsync(c => c.Name, c => c.Id);
        var defs = new (string Name, string Category, AttributeDataType Type, bool BuiltIn, string? Options)[]
        {
            ("Me.BirthDate", "Me", AttributeDataType.Date, true, null),
            ("Me.Phone", "Me", AttributeDataType.String, true, null),
            ("Me.City", "Me", AttributeDataType.String, true, null),
            ("Me.Photo", "Me", AttributeDataType.Image, true, null),
            ("Me.About", "Me", AttributeDataType.Text, true, null),
            ("IELTS Band", "Languages", AttributeDataType.Dropdown, true,
                """{"choices":["0","0.5","1","1.5","2","2.5","3","3.5","4","4.5","5","5.5","6","6.5","7","7.5","8","8.5","9"]}"""),
        };
        foreach (var (name, category, type, builtIn, options) in defs)
            if (!await db.AttributeDefinitions.AnyAsync(d => d.Name == name))
                db.AttributeDefinitions.Add(new AttributeDefinition
                {
                    Id = Guid.NewGuid(),
                    CategoryId = categories[category],
                    Name = name,
                    DataType = type,
                    IsBuiltIn = builtIn,
                    OptionsJson = options,
                });
        await db.SaveChangesAsync();
    }

    private static async Task EnsureDemoPositionAsync(IAppDbContext db)
    {
        const string title = "Senior .NET Developer";
        if (!await db.Positions.AnyAsync(p => p.Title == title))
            db.Positions.Add(new Position
            {
                Id = Guid.NewGuid(),
                Title = title,
                ShortDescription = "Demo position for local development.",
                Company = "Demo Corp",
                Level = "Senior",
                IsPublic = true,
                MaxProjects = 3,
            });
    }
}
