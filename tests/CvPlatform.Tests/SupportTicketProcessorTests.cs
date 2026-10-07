using System.Text;
using AwesomeAssertions;
using CvPlatform.Core.Support;
using CvPlatform.Infrastructure.Support;

namespace CvPlatform.Tests;

public class SupportTicketProcessorTests
{
    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    [Fact]
    public void CompleteTicketIsValid()
    {
        var json = """
            {"Reported by":"a@b.c (Admin)","Position":"Dev","Link":"https://x/y","Priority":"High","Summary":"Broken"}
            """;

        SupportTicketProcessor.TryRead(Bytes(json), out var ticket, out var reason).Should().BeTrue();
        reason.Should().BeEmpty();
        ticket!.Summary.Should().Be("Broken");
        ticket.ReportedBy.Should().Be("a@b.c (Admin)");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"Summary":"   ","Priority":"Low"}""")]
    [InlineData("""{"Summary":"Broken"}""")]
    [InlineData("""{"Summary":"Broken","Priority":" "}""")]
    public void MalformedTicketIsRejected(string json)
    {
        SupportTicketProcessor.TryRead(Bytes(json), out _, out var reason).Should().BeFalse();
        reason.Should().NotBeEmpty();
    }

    [Fact]
    public void LegacyPayloadWithoutAdminsStillDeserializes()
    {
        var json = """
            {"Reported by":"a@b.c (Admin)","Link":"https://x/y","Priority":"Low","Summary":"Old","Admins":["attacker@evil.test"]}
            """;

        SupportTicketProcessor.TryRead(Bytes(json), out var ticket, out _).Should().BeTrue();
        ticket!.Summary.Should().Be("Old");
    }

    [Fact]
    public void RecipientsComeFromConfigurationNotPayload()
    {
        var admins = new SupportOptions { AdminEmails = " ops@hirely.test, admin@hirely.test " };

        admins.AdminEmailList.Should().Equal("ops@hirely.test", "admin@hirely.test");
    }
}