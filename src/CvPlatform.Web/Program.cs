using System.Globalization;
using System.Threading.RateLimiting;
using CvPlatform.Core.Storage;
using Blazored.LocalStorage;
using CvPlatform.Application;
using CvPlatform.Application.Markdown;
using CvPlatform.Core.Entities;
using CvPlatform.Application.Exports;
using CvPlatform.Infrastructure.Data;
using CvPlatform.Infrastructure.Markdown;
using CvPlatform.Infrastructure.Exports;
using CvPlatform.Web.Exports;
using CvPlatform.Web.Auth;
using CvPlatform.Web.Configuration;
using CvPlatform.Web.Storage;
using CvPlatform.Application.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
var applicationConfiguration = builder.Services.AddCvPlatformConfiguration(builder.Configuration);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/cvplatform-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddCvPlatformDatabase(applicationConfiguration.ConnectionStrings.Default);
builder.Services.AddCvPlatformApplication();
builder.Services.AddSingleton<IMarkdownRenderer, MarkdownRenderer>();
builder.Services.AddTransient<IExportService, ExportService>();
builder.Services.AddTransient<CvPlatform.Infrastructure.Exports.IProfileImageFetcher,
    CvPlatform.Infrastructure.Exports.ProfileImageFetcher>();
builder.Services.AddTransient<CvPlatform.Application.Users.IUserAdministrationService,
    CvPlatform.Web.Users.UserAdministrationService>();
builder.Services.AddSingleton<IImageStorage, CvPlatform.Infrastructure.Storage.B2ImageStorage>();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");

var gmail = applicationConfiguration.Gmail;
if (gmail.IsConfigured)
    builder.Services.AddTransient<CvPlatform.Core.Email.IAppEmailSender,
        CvPlatform.Infrastructure.Email.GmailEmailSender>();
else
    builder.Services.AddTransient<CvPlatform.Core.Email.IAppEmailSender,
        CvPlatform.Infrastructure.Email.NoOpEmailSender>();

builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = gmail.RequireConfirmedAccount && gmail.IsConfigured;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

var google = applicationConfiguration.Authentication.Google;
var facebook = applicationConfiguration.Authentication.Facebook;

var authentication = builder.Services.AddAuthentication();
if (google.IsConfigured)
    authentication.AddGoogle(o =>
    {
        o.ClientId = google.ClientId;
        o.ClientSecret = google.ClientSecret;
    });
if (facebook.IsConfigured)
    authentication.AddFacebook(o =>
    {
        o.AppId = facebook.AppId;
        o.AppSecret = facebook.AppSecret;
    });

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Candidate", policy => policy.RequireRole(Roles.Candidate));
    options.AddPolicy("PositionManagement", policy => policy.RequireRole(Roles.Admin, Roles.Recruiter));
});
builder.Services.AddMudServices();
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddScoped<CvPlatform.Web.State.ProfileHeaderState>();
builder.Services.AddLocalization();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    o.AddPolicy("uploads", context => RateLimitPartition.GetFixedWindowLimiter(
        $"upload:{context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous"}",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    o.AddPolicy("image-downloads", context => RateLimitPartition.GetFixedWindowLimiter(
        $"image-download:{context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous"}",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    var cultures = new[] { new CultureInfo("en"), new CultureInfo("pl") };
    o.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en");
    o.SupportedCultures = cultures;
    o.SupportedUICultures = cultures;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHttpsRedirection();
}

app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseSerilogRequestLogging();

app.MapStaticAssets();
app.MapAuthEndpoints();
app.MapB2ImageUploadEndpoints();
app.MapProfileImageEndpoints();
app.MapExportEndpoints();
app.MapRazorComponents<CvPlatform.Web.Components.App>()
    .AddInteractiveServerRenderMode();

var skipMigrate = applicationConfiguration.Database.SkipMigrate;
if (!skipMigrate)
{
    using var scope = app.Services.CreateScope();
    var dbFactory = scope.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

await CvPlatform.Web.Seed.SeedData.SeedAsync(app.Services, applicationConfiguration.Seed);

app.Run();
