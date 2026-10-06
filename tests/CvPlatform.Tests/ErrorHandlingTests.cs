using AwesomeAssertions;
using CvPlatform.Application.Common;
using CvPlatform.Web.ErrorHandling;
using CvPlatform.Web.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Npgsql;
using System.Globalization;

namespace CvPlatform.Tests;

public class ErrorMappingTests
{
    [Theory]
    [InlineData(typeof(DbUpdateConcurrencyException), 409, ErrorCodes.ConcurrencyConflict)]
    [InlineData(typeof(KeyNotFoundException), 404, ErrorCodes.NotFound)]
    [InlineData(typeof(UnauthorizedAccessException), 403, ErrorCodes.Forbidden)]
    [InlineData(typeof(TimeoutException), 503, ErrorCodes.ServiceUnavailable)]
    [InlineData(typeof(InvalidOperationException), 500, ErrorCodes.Unexpected)]
    [InlineData(typeof(HttpRequestException), 503, ErrorCodes.ServiceUnavailable)]
    [InlineData(typeof(FormatException), 400, ErrorCodes.ValidationFailed)]
    public void Known_exception_types_map_to_a_status_and_a_stable_code(
        Type exceptionType,
        int expectedStatus,
        string expectedCode)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "boom")!;

        var mapping = ErrorMapping.Of(exception);

        mapping.StatusCode.Should().Be(expectedStatus);
        mapping.Code.Should().Be(expectedCode);
    }

    [Fact]
    public void A_unique_constraint_violation_is_a_conflict_not_a_server_fault()
    {
        var exception = new DbUpdateException(
            "insert failed",
            new PostgresException("duplicate key", "ERROR", "ERROR", "23505"));

        var mapping = ErrorMapping.Of(exception);

        mapping.StatusCode.Should().Be(409);
        mapping.Code.Should().Be(ErrorCodes.Conflict);
        mapping.IsServerFault.Should().BeFalse();
    }

    [Fact]
    public void Other_database_failures_stay_server_faults()
    {
        var exception = new DbUpdateException(
            "insert failed",
            new InvalidOperationException("connection reset"));

        var mapping = ErrorMapping.Of(exception);

        mapping.StatusCode.Should().Be(500);
        mapping.Code.Should().Be(ErrorCodes.Unexpected);
        mapping.IsServerFault.Should().BeTrue();
    }

    [Fact]
    public void Antiforgery_failures_are_reported_as_validation_problems()
    {
        var mapping = ErrorMapping.Of(
            new Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException("bad token"));

        mapping.StatusCode.Should().Be(400);
        mapping.Code.Should().Be(ErrorCodes.ValidationFailed);
        mapping.IsServerFault.Should().BeFalse();
    }
}

public class ProblemResultsTests
{
    [Theory]
    [InlineData(ErrorCodes.NotFound, 404)]
    [InlineData(ErrorCodes.Forbidden, 403)]
    [InlineData(ErrorCodes.Unauthorized, 401)]
    [InlineData(ErrorCodes.ValidationFailed, 400)]
    [InlineData(ErrorCodes.Conflict, 409)]
    [InlineData(ErrorCodes.ConcurrencyConflict, 409)]
    [InlineData(ErrorCodes.RateLimited, 429)]
    [InlineData(ErrorCodes.ServiceUnavailable, 503)]
    [InlineData(ErrorCodes.Unexpected, 500)]
    public void Every_error_code_maps_to_a_sane_status(string code, int expected)
    {
        ProblemResults.StatusFor(code).Should().Be(expected);
    }

    [Fact]
    public void The_problem_carries_the_error_code_and_a_detail_free_summary()
    {
        var problem = Extract(ProblemResults.From(new Error(ErrorCodes.NotFound, "secret internal note")));

        problem.Status.Should().Be(404);
        problem.Detail.Should().BeNull();
        problem.Extensions[ProblemFactory.ErrorCodeKey].Should().Be(ErrorCodes.NotFound);
        problem.Title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validation_failures_keep_the_actionable_validator_message()
    {
        var problem = Extract(ProblemResults.From(
            new Error(ErrorCodes.ValidationFailed, "Title must not be empty.")));

        problem.Detail.Should().Be("Title must not be empty.");
    }

    [Theory]
    [InlineData(ErrorCodes.NotFound, "Template was not found.")]
    [InlineData(ErrorCodes.Forbidden, "Only recruiters can manage position templates.")]
    [InlineData(ErrorCodes.ConcurrencyConflict, "The template was modified by someone else.")]
    public void Non_validation_codes_never_leak_the_developer_message(string code, string message)
    {
        var problem = Extract(ProblemResults.From(new Error(code, message)));

        problem.Detail.Should().BeNull();
        problem.Title.Should().NotContain(message);
    }

    [Theory]
    [InlineData(400, ErrorCodes.ValidationFailed)]
    [InlineData(401, ErrorCodes.Unauthorized)]
    [InlineData(403, ErrorCodes.Forbidden)]
    [InlineData(404, ErrorCodes.NotFound)]
    [InlineData(409, ErrorCodes.Conflict)]
    [InlineData(429, ErrorCodes.RateLimited)]
    [InlineData(503, ErrorCodes.ServiceUnavailable)]
    [InlineData(500, ErrorCodes.Unexpected)]
    public void Status_codes_map_back_to_error_codes(int status, string expected)
    {
        ProblemFactory.CodeForStatus(status).Should().Be(expected);
    }

    private static ProblemDetails Extract(IResult result) =>
        result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;
}

public class ResponseNegotiationTests
{
    [Fact]
    public void Api_requests_always_get_problem_details()
    {
        var context = Context("/api/exports/cvs/1/pdf", "text/html,application/xhtml+xml");

        ResponseNegotiation.IsApiRequest(context).Should().BeTrue();
        ResponseNegotiation.ExpectsProblemDetails(context).Should().BeTrue();
    }

    [Fact]
    public void Browser_page_requests_get_html()
    {
        var context = Context("/positions", "text/html,application/xhtml+xml,*/*;q=0.8");

        ResponseNegotiation.ExpectsProblemDetails(context).Should().BeFalse();
    }

    [Fact]
    public void Requests_without_an_accept_header_get_problem_details()
    {
        ResponseNegotiation.ExpectsProblemDetails(Context("/positions", null)).Should().BeTrue();
    }

    [Fact]
    public void Xhr_requests_get_problem_details()
    {
        ResponseNegotiation.ExpectsProblemDetails(Context("/positions", "application/json")).Should().BeTrue();
    }

    private static DefaultHttpContext Context(string path, string? accept)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (accept is not null)
            context.Request.Headers.Accept = accept;
        return context;
    }
}

public class ErrorMessageLocalizerTests
{
    [Fact]
    public void Every_error_code_has_a_message_in_english_and_polish()
    {
        foreach (var code in ErrorCodes.All)
        {
            var key = KeyFor(code);
            Resource("en", key).Found.Should().BeTrue(
                "'{0}' is missing from SharedResource.resx", key);
            Resource("pl", key).Found.Should().BeTrue(
                "'{0}' is missing from SharedResource.pl.resx", key);
        }
    }

    [Fact]
    public void Each_error_code_produces_friendly_text_without_developer_detail()
    {
        var messages = Messages(Environments.Production);

        foreach (var code in ErrorCodes.All.Where(code => code != ErrorCodes.ValidationFailed))
        {
            var text = messages.Describe(new Error(code, "internal detail"));

            text.Should().NotBeNullOrWhiteSpace();
            text.Should().NotContain("internal detail");
        }
    }

    [Fact]
    public void Validation_failures_keep_the_actionable_detail()
    {
        var text = Messages(Environments.Development)
            .Describe(new Error(ErrorCodes.ValidationFailed, "Title must not be empty."));

        text.Should().Contain("Title must not be empty.");
    }

    [Fact]
    public void Unknown_codes_never_expose_the_developer_message_in_production()
    {
        var messages = Messages(Environments.Production);

        var text = messages.Describe(new Error("brand_new_code", "the connection string is secret"));

        text.Should().NotContain("secret");
        text.Should().Be(messages.Describe(new Error(ErrorCodes.Unexpected, "anything")));
    }

    [Fact]
    public void Unknown_codes_show_the_developer_message_in_development()
    {
        var text = Messages(Environments.Development)
            .Describe(new Error("brand_new_code", "NullReference at Foo.Bar"));

        text.Should().Contain("NullReference at Foo.Bar");
    }

    [Fact]
    public void Exceptions_are_never_described_with_their_message_in_production()
    {
        var text = Messages(Environments.Production)
            .Describe(new InvalidOperationException("connection string is secret"));

        text.Should().NotContain("secret");
    }

    [Theory]
    [InlineData("invalid_type", "ImageUploadTypeInvalid")]
    [InlineData("too_large", "ImageUploadTooLarge")]
    [InlineData("ticket_failed", "ImageUploadTicketFailed")]
    [InlineData("storage_rejected", "ImageUploadRejected")]
    [InlineData("verify_failed", "ImageUploadVerifyFailed")]
    [InlineData("something_new", "ImageUploadFailed")]
    public void Upload_failure_codes_map_to_their_own_message(string code, string expectedKey)
    {
        var text = Messages(Environments.Production).DescribeUploadFailure(code);

        text.Should().Be(Resource("en", expectedKey).Value);
    }

    [Theory]
    [InlineData("ImageUploadTypeInvalid")]
    [InlineData("ImageUploadTooLarge")]
    [InlineData("ImageUploadTicketFailed")]
    [InlineData("ImageUploadRejected")]
    [InlineData("ImageUploadVerifyFailed")]
    [InlineData("ImageUploadFailed")]
    public void Every_image_upload_message_is_localized_in_english_and_polish(string key)
    {
        Resource("en", key).Found.Should().BeTrue(
            "'{0}' is missing from SharedResource.resx", key);
        Resource("pl", key).Found.Should().BeTrue(
            "'{0}' is missing from SharedResource.pl.resx", key);
    }

    private readonly record struct ResourceLookup(bool Found, string Value);

    private static string KeyFor(string code) => code switch
    {
        ErrorCodes.NotFound => "ErrorNotFound",
        ErrorCodes.Forbidden => "ErrorForbidden",
        ErrorCodes.Unauthorized => "ErrorUnauthorized",
        ErrorCodes.ValidationFailed => "ErrorValidation",
        ErrorCodes.Conflict => "ErrorConflict",
        ErrorCodes.ConcurrencyConflict => "ErrorConcurrency",
        ErrorCodes.RateLimited => "ErrorRateLimited",
        ErrorCodes.ServiceUnavailable => "ErrorServiceUnavailable",
        _ => "ErrorUnexpected"
    };

    private static ErrorMessageLocalizer Messages(string environmentName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddCvErrorHandling(environmentName);
        return services.BuildServiceProvider().GetRequiredService<ErrorMessageLocalizer>();
    }

    private static ResourceLookup Resource(string culture, string key)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var localized = SharedLocalizer()[key];
            return new ResourceLookup(!localized.ResourceNotFound, localized.Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static IStringLocalizer<SharedResource> SharedLocalizer() =>
        new ServiceCollection()
            .AddSingleton<IHostEnvironment>(
                ErrorHandlingTestServices.CreateHostEnvironment(Environments.Production))
            .AddLogging()
            .AddLocalization()
            .BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<SharedResource>>();
}