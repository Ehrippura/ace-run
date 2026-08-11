using ace_run.Services;
using Xunit;

namespace ace_run.Tests;

public class UrlUtilTests
{
    // --- TryNormalize: what counts as a launchable URL ---

    [Theory]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("http://example.com/path?q=1", "http://example.com/path?q=1")]
    // Bare host: the whole point of the helper — the user types what they would say out loud.
    [InlineData("example.com", "https://example.com")]
    [InlineData("www.example.com/a/b", "https://www.example.com/a/b")]
    // Host:port must not be read as a scheme named "example.com", which Uri would otherwise
    // accept quite happily.
    [InlineData("example.com:8080/path", "https://example.com:8080/path")]
    // Non-http schemes are the reason ItemKind.Url exists at all.
    [InlineData("steam://run/440", "steam://run/440")]
    [InlineData("mailto:someone@example.com", "mailto:someone@example.com")]
    [InlineData("ms-settings:display", "ms-settings:display")]
    // Surrounding whitespace is trimmed, not rejected — it is what pasting produces.
    [InlineData("  https://example.com  ", "https://example.com")]
    public void TryNormalize_accepts(string input, string expected)
    {
        Assert.True(UrlUtil.TryNormalize(input, out var url));
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // No dot and no scheme: a bare word is a search term, not a host.
    [InlineData("example")]
    // A space rules out the bare-host guess; without a scheme there is nothing left to try.
    [InlineData("hello world")]
    // file: belongs to an App item — that is the one absolute URI shape deliberately refused.
    [InlineData("file:///C:/Windows/notepad.exe")]
    public void TryNormalize_rejects(string? input)
    {
        Assert.False(UrlUtil.TryNormalize(input, out var url));
        Assert.Equal(string.Empty, url);
    }

    [Fact]
    public void TryNormalize_leaves_an_explicit_scheme_alone()
    {
        // Already absolute, so no https:// is prepended even though a dot is present.
        Assert.True(UrlUtil.TryNormalize("ftp://files.example.com", out var url));
        Assert.Equal("ftp://files.example.com", url);
    }

    // --- SuggestDisplayName ---

    [Theory]
    [InlineData("https://example.com", "example.com")]
    [InlineData("https://www.example.com/a/b", "example.com")]
    [InlineData("https://WWW.Example.COM", "example.com")]
    // Subdomains other than www stay: they are how the user tells the sites apart.
    [InlineData("https://docs.example.com", "docs.example.com")]
    public void SuggestDisplayName_uses_the_host(string url, string expected)
        => Assert.Equal(expected, UrlUtil.SuggestDisplayName(url));

    [Theory]
    // Hostless schemes have nothing better to offer than the URL itself.
    [InlineData("ms-settings:display")]
    // Unparseable input falls through unchanged rather than throwing.
    [InlineData("not a url")]
    public void SuggestDisplayName_falls_back_to_the_raw_string(string url)
        => Assert.Equal(url, UrlUtil.SuggestDisplayName(url));

    [Fact]
    public void SuggestDisplayName_names_a_mailto_after_its_mail_domain()
    {
        // Uri parses mailto: as an authority-bearing scheme, so Host is the part after the @
        // rather than empty. The result is a defensible name for the item, but it is not the
        // "falls back to the raw string" case the method's own comment used to claim it was.
        Assert.Equal("example.com", UrlUtil.SuggestDisplayName("mailto:someone@example.com"));
    }
}
