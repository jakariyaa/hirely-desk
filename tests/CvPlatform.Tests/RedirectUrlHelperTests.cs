using CvPlatform.Core.Security;
using AwesomeAssertions;

namespace CvPlatform.Tests;

public class RedirectUrlHelperTests
{
    [Theory]
    [InlineData("/", true)]
    [InlineData("/Account/Login", true)]
    [InlineData("/a?b=c", true)]
    public void Local_urls_pass(string url, bool expected)
    {
        RedirectUrlHelper.IsLocalUrl(url).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://evil.example", false)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Non_local_urls_fail(string? url, bool expected)
    {
        RedirectUrlHelper.IsLocalUrl(url).Should().Be(expected);
    }

    [Fact]
    public void SafeReturnUrl_falls_back_to_home()
    {
        RedirectUrlHelper.SafeReturnUrl("https://evil.example").Should().Be("/");
        RedirectUrlHelper.SafeReturnUrl(null).Should().Be("/");
        RedirectUrlHelper.SafeReturnUrl("/Account/Login").Should().Be("/Account/Login");
    }
}
