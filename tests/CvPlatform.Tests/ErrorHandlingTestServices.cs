using CvPlatform.Web.ErrorHandling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;

namespace CvPlatform.Tests;

/// <summary>Registers the client-side error handling services for component and unit tests.</summary>
internal static class ErrorHandlingTestServices
{
    public const string Production = "Production";
    public const string Development = "Development";
    public const string WebAssemblyName = "CvPlatform.Web";

    public static IServiceCollection AddCvErrorHandling(
        this IServiceCollection services,
        string environmentName = Production)
    {
        services.AddSingleton<IHostEnvironment>(CreateHostEnvironment(environmentName));
        services.AddLogging();
        services.AddLocalization();
        services.AddScoped<ErrorMessageLocalizer>();
        services.AddScoped<IUiErrorReporter, UiErrorReporter>();
        return services;
    }

    /// <summary>
    /// The resource localizer resolves satellite assemblies through
    /// <see cref="IHostEnvironment.ApplicationName"/>, so tests must pretend to be the web app.
    /// </summary>
    public static IHostEnvironment CreateHostEnvironment(string environmentName) =>
        new TestHostEnvironment(environmentName);

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = WebAssemblyName;

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}