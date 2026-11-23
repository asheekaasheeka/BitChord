using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace BitChord.Windows;

// Settings and policy hooks shared by the main window and every popup.
static class Browser
{
    // The player runs from the files bundled beside the exe, not from any BitChord website.
    public static readonly Uri StartUri = new($"https://{NavigationPolicy.AppHost}/");

    public static void Configure(CoreWebView2 core)
    {
        var s = core.Settings;
#if DEBUG
        s.AreDevToolsEnabled = true;
#else
        s.AreDevToolsEnabled = false;
        s.AreDefaultContextMenusEnabled = false;
#endif
        s.AreHostObjectsAllowed = false;
        s.IsWebMessageEnabled = false;
        s.IsStatusBarEnabled = false;

        // Both events also fire for server redirects (same navigation id, IsRedirected), so redirects get the same policy.
        core.NavigationStarting += OnNavigationStarting;
        core.FrameNavigationStarting += OnFrameNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.PermissionRequested += OnPermissionRequested;
        core.DownloadStarting += (_, e) => { e.Cancel = true; e.Handled = true; };
    }

    // Serves https://app.bitchord.example/ from AppPaths.WebFolder. Not SetVirtualHostNameToFolderMapping:
    // WebResourceRequested never fires for a mapped host (checked at runtime, not documented), so client routes
    // (/home, /album/:id) could not fall back to index.html on a reload or deep link.
    // The host has no DNS, so nothing reaches the network. Per CoreWebView2: call before the first
    // navigation, and for a popup after NewWindow is set (WebResourceRequested docs).
    public static void ServeApp(CoreWebView2 core)
    {
        // Document source = pages, iframes and dedicated workers (the AutoMix worker). No service or shared workers.
        core.AddWebResourceRequestedFilter($"https://{NavigationPolicy.AppHost}/*",
            CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.Document);
        core.WebResourceRequested += OnWebResourceRequested;
    }

    static void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals(NavigationPolicy.AppHost, StringComparison.OrdinalIgnoreCase)) return;
        var env = ((CoreWebView2)sender!).Environment;
        var file = AppFiles.Find(AppPaths.WebFolder, uri);
        // A page with no file is a client route: give it index.html, as the Pages site does. Assets still 404.
        if (file == null && e.ResourceContext == CoreWebView2WebResourceContext.Document)
            file = AppFiles.Find(AppPaths.WebFolder, new Uri(StartUri, "index.html"));
        try
        {
            // No Access-Control-Allow-Origin: other origins may embed these files but never fetch them.
            // shortcut: the FileStream closes when GC collects it, not when the response ends; wrap it if open handles ever block an update.
            e.Response = file == null
                ? env.CreateWebResourceResponse(null, 404, "Not Found", "")
                : env.CreateWebResourceResponse(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete),
                    200, "OK", $"Content-Type: {AppFiles.ContentType(file)}\nX-Content-Type-Options: nosniff");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "");
        }
    }

    // Our installed extension's id, set by MainForm after install; its pages are the only chrome-extension:// allowed.
    public static string? ExtensionId { get; set; }

    static NavigationDecision Decide(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? NavigationPolicy.Decide(uri, ExtensionId) : NavigationDecision.Block;

    static void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var decision = Decide(e.Uri);
        if (decision == NavigationDecision.Allow) return;
        e.Cancel = true;
        // Only a user click may open the default browser; script and timer navigations are just dropped.
        if (decision == NavigationDecision.OpenExternal && e.IsUserInitiated) OpenExternal(e.Uri);
        else Log("blocked navigation", e.Uri);
    }

    // Subframes (iframes) are not seen by NavigationStarting. Third-party https embeds stay allowed;
    // only Block schemes are cancelled, and a frame never launches anything outside the app.
    static void OnFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri is "about:blank" or "about:srcdoc") return; // empty or inline iframes inherit their parent; pages rely on them
        if (Decide(e.Uri) != NavigationDecision.Block) return;
        e.Cancel = true;
        Log("blocked frame", e.Uri);
    }

    static async void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true; // never let WebView2 open its own unmanaged window
        // window.open('') opens about:blank; whatever the opener loads into it later passes the popup's own policy hooks.
        var decision = e.Uri is "" or "about:blank" ? NavigationDecision.Allow : Decide(e.Uri);
        if (decision == NavigationDecision.OpenExternal && e.IsUserInitiated) { OpenExternal(e.Uri); return; }
        if (decision != NavigationDecision.Allow) { Log("blocked popup", e.Uri); return; }

        CoreWebView2Deferral? deferral = null;
        PopupForm? popup = null;
        try
        {
            deferral = e.GetDeferral();
            popup = new PopupForm();
            popup.Show();
            var core = await popup.InitAsync(((CoreWebView2)sender!).Environment);
            e.NewWindow = core;
            ServeApp(core);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"popup init failed: {ex.GetType().Name}");
            popup?.Close();
        }
        finally
        {
            deferral?.Complete();
        }
    }

    // Deny by default; music playback only needs autoplay.
    static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e) =>
        e.State = e.PermissionKind == CoreWebView2PermissionKind.Autoplay
            ? CoreWebView2PermissionState.Allow
            : CoreWebView2PermissionState.Deny;

    static void OpenExternal(string url)
    {
        // Re-check the scheme here so a policy bug can never shell-open file: or a custom protocol.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return;
        Log("open external", url);
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    // Host only: full URLs can carry tokens in the query or fragment.
    static void Log(string what, string url) =>
        Debug.WriteLine($"{what}: {(Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "?")}");
}
