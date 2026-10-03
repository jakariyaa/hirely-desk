using System.Globalization;
using System.Threading.RateLimiting;
using CvPlatform.Core.Storage;
using Blazored.LocalStorage;
using CvPlatform.Application;
using CvPlatform.Application.Integration;
using CvPlatform.Application.Markdown;
using CvPlatform.Core.Entities;
using CvPlatform.Application.Exports;
using CvPlatform.Infrastructure.Data;
using CvPlatform.Infrastructure.Markdown;
using CvPlatform.Infrastructure.Exports;
using CvPlatform.Web.Exports;
using CvPlatform.Web.Auth;
using CvPlatform.Web.Configuration;
using CvPlatform.Web.Integration;
using CvPlatform.Web.Storage;
using CvPlatform.Application.Authorization;
using CvPlatform.Web.ErrorHandling;
using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
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
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Instance =
        $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
    context.ProblemDetails.Extensions["traceId"] = ErrorMapping.TraceIdOf(context.HttpContext);
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddScoped<ErrorMessageLocalizer>();
builder.Services.AddScoped<IUiErrorReporter, UiErrorReporter>();

var gmail = applicationConfiguration.Gmail;
if (gmail.IsConfigured)
    builder.Services.AddTransient<CvPlatform.Core.Email.IAppEmailSender,
        CvPlatform.Infrastructure.Email.GmailEmailSender>();
else
    builder.Services.AddTransient<CvPlatform.Core.Email.IAppEmailSender,
        CvPlatform.Infrastructure.Email.NoOpEmailSender>();

var salesforce = applicationConfiguration.Salesforce;
if (salesforce.IsConfigured)
{
    builder.Services.AddSingleton<CvPlatform.Infrastructure.Crm.SalesforceTokenProvider>();
    builder.Services.AddHttpClient<CvPlatform.Core.Crm.ICrmService,
            CvPlatform.Infrastructure.Crm.SalesforceCrmService>();
}
else
    builder.Services.AddTransient<CvPlatform.Core.Crm.ICrmService,
        CvPlatform.Infrastructure.Crm.NoOpCrmService>();

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
authentication.AddScheme<AuthenticationSchemeOptions, PositionApiTokenAuthenticationHandler>(
    PositionApiTokenDefaults.Scheme, displayName: null, configureOptions: null);
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
    options.AddPolicy(PositionApiTokenDefaults.Policy, policy => policy
        .AddAuthenticationSchemes(PositionApiTokenDefaults.Scheme)
        .RequireAuthenticatedUser());
});
builder.Services.AddMudServices();
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddScoped<CvPlatform.Web.State.ProfileHeaderState>();
builder.Services.AddLocalization();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
        return ValueTask.CompletedTask;
    };
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
    o.AddPolicy("position-api", context => RateLimitPartition.GetFixedWindowLimiter(
        PositionApiPartition(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

static string PositionApiPartition(HttpContext context)
{
    // The raw token must not become an in-memory dictionary key or a log-friendly value,
    // so the partition uses its hash; malformed or missing credentials share the
    // remote-IP partition instead of creating a new one per arbitrary header value.
    if (PositionApiTokenDefaults.TryReadBearer(context.Request, out var token) &&
        PositionApiTokenSecret.HasTokenPrefix(token))
        return $"position-api:{PositionApiTokenSecret.Hash(token)}";

    return $"position-api:{context.Connection.RemoteIpAddress?.ToString() ?? "anonymous"}";
}
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    var cultures = new[] { new CultureInfo("en"), new CultureInfo("pl") };
    o.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en");
    o.SupportedCultures = cultures;
    o.SupportedUICultures = cultures;
});

var app = builder.Build();

// One exception pipeline in every environment: registered IExceptionHandlers run first, the
// /Error fallback renders for browser requests. Development adds exception detail (never a raw
// page) through GlobalExceptionHandler, so diagnostics stay in logs/problem details too.
app.UseExceptionHandler("/Error", createScopeForErrors: true);
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);
app.UseStatusCodePages(StatusCodePageWriter.WriteAsync);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapAuthEndpoints();
app.MapB2ImageUploadEndpoints();
app.MapProfileImageEndpoints();
app.MapExportEndpoints();
app.MapPositionSummaryEndpoints();
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
