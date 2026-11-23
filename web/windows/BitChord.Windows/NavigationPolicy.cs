namespace BitChord.Windows;

public enum NavigationDecision { Allow, OpenExternal, Block }

public static class NavigationPolicy
{
    // Virtual host for the bundled web player (<exe dir>\web, see Browser.cs). .example is reserved (RFC 6761),
    // so no real site can ever answer on it. Must match the extension's site origins (build.ps1).
    public const string AppHost = "app.bitchord.example";

    // The bundled player plus the login hosts. No public BitChord site: bitchord.in (WordPress landing site)
    // and bitchord-8bw.pages.dev open in the default browser like any other site.
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        AppHost,
        "accounts.google.com", "accounts.youtube.com", "music.youtube.com", "www.youtube.com",
        "accounts.spotify.com", "open.spotify.com", "discord.com",
    };

    // Main-frame navigations, subframes and new-window requests go through this.
    // ownExtensionId is our installed extension's id; null (not installed yet) blocks every extension page.
    public static NavigationDecision Decide(Uri uri, string? ownExtensionId)
    {
        if (uri is null || !uri.IsAbsoluteUri) return NavigationDecision.Block;

        if (uri.Scheme == "chrome-extension")
            return ownExtensionId != null && uri.Host.Equals(ownExtensionId, StringComparison.OrdinalIgnoreCase)
                ? NavigationDecision.Allow
                : NavigationDecision.Block;

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return uri.UserInfo.Length == 0 && uri.IsDefaultPort && AllowedHosts.Contains(uri.IdnHost)
                ? NavigationDecision.Allow
                : NavigationDecision.OpenExternal;
        }

        return uri.Scheme == Uri.UriSchemeHttp ? NavigationDecision.OpenExternal : NavigationDecision.Block;
    }
}
