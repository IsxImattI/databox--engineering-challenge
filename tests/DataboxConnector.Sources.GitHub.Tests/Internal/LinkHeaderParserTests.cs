using DataboxConnector.Sources.GitHub.Internal;
using FluentAssertions;
using Xunit;

namespace DataboxConnector.Sources.GitHub.Tests.Internal;

public class LinkHeaderParserTests
{
    [Fact]
    public void GetUrl_NullHeader_ReturnsNull()
    {
        LinkHeaderParser.GetUrl(null, "next").Should().BeNull();
    }

    [Fact]
    public void GetUrl_EmptyHeader_ReturnsNull()
    {
        LinkHeaderParser.GetUrl("   ", "next").Should().BeNull();
    }

    [Fact]
    public void GetUrl_NextLink_ReturnsUri()
    {
        var header = "<https://api.github.com/repos/o/r/commits?page=2>; rel=\"next\", " +
                     "<https://api.github.com/repos/o/r/commits?page=10>; rel=\"last\"";

        var result = LinkHeaderParser.GetUrl(header, "next");

        result.Should().NotBeNull();
        result!.ToString().Should().Be("https://api.github.com/repos/o/r/commits?page=2");
    }

    [Fact]
    public void GetUrl_LastLink_ReturnsUri()
    {
        var header = "<https://api.github.com/repos/o/r/commits?page=2>; rel=\"next\", " +
                     "<https://api.github.com/repos/o/r/commits?page=10>; rel=\"last\"";

        var result = LinkHeaderParser.GetUrl(header, "last");

        result.Should().NotBeNull();
        result!.ToString().Should().Be("https://api.github.com/repos/o/r/commits?page=10");
    }

    [Fact]
    public void GetUrl_MissingRel_ReturnsNull()
    {
        var header = "<https://api.github.com/repos/o/r/commits?page=2>; rel=\"next\"";

        LinkHeaderParser.GetUrl(header, "prev").Should().BeNull();
    }

    [Fact]
    public void GetUrl_RelMatchIsCaseInsensitive()
    {
        var header = "<https://api.github.com/x>; rel=\"NEXT\"";

        LinkHeaderParser.GetUrl(header, "next").Should().NotBeNull();
    }

    [Fact]
    public void GetUrl_OnlyOneLinkInHeader_ReturnsIt()
    {
        var header = "<https://api.github.com/repos/o/r/commits?page=2>; rel=\"next\"";

        var result = LinkHeaderParser.GetUrl(header, "next");

        result.Should().NotBeNull();
    }
}