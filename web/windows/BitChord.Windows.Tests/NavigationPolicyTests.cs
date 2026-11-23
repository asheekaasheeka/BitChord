using BitChord.Windows;
using Xunit;

namespace BitChord.Windows.Tests;

public class NavigationPolicyTests
{
    [Theory]
    [InlineData("https://app.bitchord.example/")]
    [InlineData("https://app.bitchord.example/album/1?x=y")]
    [InlineData("https://accounts.google.com/signin")]
    [InlineData("https://accounts.youtube.com/")]
    [InlineData("https://music.youtube.com/watch?v=x")]
    [InlineData("https://www.youtube.com/")]
    [InlineData("https://accounts.spotify.com/authorize")]
    [InlineData("https://open.spotify.com/track/1")]
    [InlineData("https://discord.com/oauth2/authorize")]
    [InlineData("https://APP.BITCHORD.EXAMPLE/")]
    [InlineData("https://Accounts.Google.Com/")]
    [InlineData("https://app.bitchord.example:443/")]
    public void Allow(string url) => Assert.Equal(NavigationDecision.Allow, Decide(url));

    const string OwnId = "abcdefghijklmnopabcdefghijklmnop";

    [Theory]
    [InlineData("chrome-extension://abcdefghijklmnopabcdefghijklmnop/popup.html")]
    [InlineData("chrome-extension://ABCDEFGHIJKLMNOPABCDEFGHIJKLMNOP/popup.html")]
    public void OwnExtensionIsAllowed(string url) =>
        Assert.Equal(NavigationDecision.Allow, NavigationPolicy.Decide(new Uri(url), OwnId));

    [Theory]
    [InlineData("chrome-extension://ponmlkjihgfedcbaponmlkjihgfedcba/popup.html")]
    [InlineData("chrome-extension://abcdefghijklmnopabcdefghijklmnoq/popup.html")]
    public void OtherExtensionIsBlocked(string url) =>
        Assert.Equal(NavigationDecision.Block, NavigationPolicy.Decide(new Uri(url), OwnId));

    [Fact]
    public void ExtensionIsBlockedWhenNotInstalled() =>
        Assert.Equal(NavigationDecision.Block, Decide($"chrome-extension://{OwnId}/popup.html"));

    [Theory]
    [InlineData("http://app.bitchord.example/")]
    [InlineData("https://bitchord-8bw.pages.dev/")]
    [InlineData("https://app.bitchord.example.evil.com/")]
    [InlineData("https://app.bitchord.example:8443/")]
    [InlineData("http://bitchord.in/")]
    [InlineData("https://bitchord.in/")]
    [InlineData("http://accounts.google.com/")]
    [InlineData("https://bitchord.in.evil.com/")]
    [InlineData("https://evilbitchord.in/")]
    [InlineData("https://bitchord.in@evil.com/")]
    [InlineData("https://user@bitchord.in/")]
    [InlineData("https://user:pass@bitchord.in/")]
    [InlineData("https://bitchord.in:8443/")]
    [InlineData("https://youtube.com/")]
    [InlineData("https://example.com/")]
    [InlineData("http://example.com/")]
    public void OpenExternal(string url) => Assert.Equal(NavigationDecision.OpenExternal, Decide(url));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("mailto:a@bitchord.in")]
    [InlineData("about:blank")]
    [InlineData("ftp://bitchord.in/")]
    [InlineData("ms-settings:privacy")]
    public void Block(string url) => Assert.Equal(NavigationDecision.Block, Decide(url));

    [Theory]
    [InlineData("/relative/path")]
    [InlineData("bitchord.in")]
    public void RelativeUriIsBlocked(string url) =>
        Assert.Equal(NavigationDecision.Block, NavigationPolicy.Decide(new Uri(url, UriKind.Relative), OwnId));

    private static NavigationDecision Decide(string url) => NavigationPolicy.Decide(new Uri(url, UriKind.Absolute), null);
}
