using AwesomeAssertions;
using CvPlatform.Infrastructure.Email;

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
}
