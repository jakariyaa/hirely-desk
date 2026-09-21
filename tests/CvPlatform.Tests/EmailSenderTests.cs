using AwesomeAssertions;
using CvPlatform.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace CvPlatform.Tests;

public class EmailSenderTests
{
    [Theory]
    [InlineData("", "", false)]
    [InlineData("user@gmail.com", "", false)]
    [InlineData("", "secret", false)]
    [InlineData("user@gmail.com", "secret", true)]
    public void GmailOptions_reports_configuration_state(
        string address, string appPassword, bool expected)
    {
        new GmailOptions { Address = address, AppPassword = appPassword }
            .IsConfigured.Should().Be(expected);
    }

    [Fact]
    public async Task NoOp_sender_completes_without_sending()
    {
        var sender = new NoOpEmailSender(NullLogger<NoOpEmailSender>.Instance);
        await sender.Invoking(s => s.SendConfirmationLinkAsync("a@example.test", "http://localhost/confirm"))
            .Should().NotThrowAsync();
    }
}
