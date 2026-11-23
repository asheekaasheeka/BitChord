using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace BitChord.Windows.Local;

// The player's side of local files (web/app/src/data/local/library.ts).
//
// Page → app: {local:"list"} | {local:"add"} | {local:"remove", folder} | {local:"rescan"}.
// App → page: {local:"library", folders, tracks, scanning} | {local:"error", message}.
// The page only ever gets track ids and tags; the file paths stay here.
sealed class LocalBridge : IDisposable
{
    readonly Form owner;
    readonly CoreWebView2 core;
    readonly LocalLibrary library;
    bool scanning;
    bool checkedFormats;

    public LocalBridge(Form owner, CoreWebView2 core, LocalLibrary library)
    {
        this.owner = owner;
        this.core = core;
        this.library = library;
        core.WebMessageReceived += OnMessage;
    }

    static bool IsApp(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && u.Host.Equals(NavigationPolicy.AppHost, StringComparison.OrdinalIgnoreCase) && u.IsDefaultPort;

    void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsApp(e.Source)) return;
        string? type, folder = null;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("local", out var t)) return;
            type = t.GetString();
            if (doc.RootElement.TryGetProperty("folder", out var f) && f.ValueKind == JsonValueKind.String) folder = f.GetString();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return;
        }
        var folders = library.Current.Folders;
        switch (type)
        {
            case "list":
                Send();
                // A cache from before format details were scanned: fill them in once.
                if (!checkedFormats && library.Current.Tracks.Count > 0 && library.Current.Tracks.All(t => t.Codec == null)) _ = Rescan(folders);
                checkedFormats = true;
                break;
            case "rescan": _ = Rescan(folders); break;
            case "remove":
                // Only a folder already in the list; the page can't make the app read anywhere new.
                if (folder != null && folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                    _ = Rescan(folders.Where(x => !x.Equals(folder, StringComparison.OrdinalIgnoreCase)));
                break;
            case "add":
                using (var dialog = new FolderBrowserDialog { Description = "Choose a folder with music", UseDescriptionForTitle = true })
                {
                    // A user's own pick is the only way a folder gets in.
                    if (dialog.ShowDialog(owner) == DialogResult.OK) _ = Rescan(folders.Append(dialog.SelectedPath));
                }
                break;
        }
    }

    async Task Rescan(IEnumerable<string> folders)
    {
        if (scanning) return;
        scanning = true;
        Send();
        try
        {
            await library.ScanAsync(folders.ToList());
        }
        catch (Exception ex)
        {
            Post(new { local = "error", message = ex.Message });
        }
        finally
        {
            scanning = false;
            Send();
        }
    }

    void Send()
    {
        var s = library.Current;
        Post(new
        {
            local = "library",
            scanning,
            folders = s.Folders,
            tracks = s.Tracks.Select(t => new
            {
                id = t.Id, folder = t.Folder, title = t.Title, artist = t.Artist, album = t.Album, albumArtist = t.AlbumArtist,
                track = t.TrackNumber, year = t.Year, durationSec = t.DurationSec,
                codec = t.Codec, bitrateKbps = t.BitrateKbps, sampleRateHz = t.SampleRateHz, bitDepth = t.BitDepth, channels = t.Channels,
                // Shown, never used to read anything: the relative path, for telling same-named files apart.
                file = Path.GetRelativePath(t.Folder, t.Path),
            }),
        });
    }

    void Post(object message)
    {
        if (IsApp(core.Source)) core.PostWebMessageAsJson(JsonSerializer.Serialize(message));
    }

    public void Dispose() => core.WebMessageReceived -= OnMessage;
}
