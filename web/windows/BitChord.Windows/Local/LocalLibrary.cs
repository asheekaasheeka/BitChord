using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace BitChord.Windows.Local;

// The folders the user added, the audio files found in them, and serving those files to the player at
// https://app.bitchord.example/local/<id>. Only files from the last scan are served: an id is the
// only thing the page can name, never a path.
public sealed class LocalLibrary
{
    public sealed record Track(string Id, string Folder, string Path, string Title, string? Artist, string? Album,
        string? AlbumArtist, int? TrackNumber, int? Year, double? DurationSec, long Size,
        string? Codec = null, int? BitrateKbps = null, int? SampleRateHz = null, int? BitDepth = null, int? Channels = null);

    public sealed record Snapshot(List<string> Folders, List<Track> Tracks);

    public const string UrlPrefix = "/local/";
    public const string ArtPrefix = "/local-art/";

    // What Chromium (and so WebView2) decodes in an <audio> element.
    static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg",
        [".flac"] = "audio/flac",
        [".m4a"] = "audio/mp4",
        [".aac"] = "audio/aac",
        [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg",
        [".opus"] = "audio/ogg",
        [".wav"] = "audio/wav",
        [".webm"] = "audio/webm",
    };

    readonly string file;
    readonly object gate = new();
    Snapshot snapshot = new([], []);
    Dictionary<string, Track> byId = [];

    public LocalLibrary(string file)
    {
        this.file = file;
        try
        {
            if (File.Exists(file) && JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(file)) is { } saved)
                Set(saved with { Folders = saved.Folders ?? [], Tracks = saved.Tracks ?? [] }, save: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Corrupt or unreadable cache: start empty, the folders can be added again.
        }
    }

    public Snapshot Current { get { lock (gate) return snapshot; } }

    public Track? Find(string id)
    {
        lock (gate) return byId.GetValueOrDefault(id);
    }

    // The container's usual codec; an .m4a may also be ALAC, which Windows reports with a bit depth.
    static string CodecOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp3" => "MP3",
        ".flac" => "FLAC",
        ".m4a" or ".aac" => "AAC",
        ".ogg" or ".oga" => "Vorbis",
        ".opus" => "Opus",
        ".wav" => "PCM",
        ".webm" => "WebM",
        var other => other.TrimStart('.').ToUpperInvariant(),
    };

    public static bool IsAudio(string path) => Types.ContainsKey(Path.GetExtension(path));

    public static string IdOf(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))[..16].ToLowerInvariant();

    // Scans [folders] again (the slow part runs off the UI thread) and keeps the result.
    public async Task<Snapshot> ScanAsync(IEnumerable<string> folders, CancellationToken cancel = default)
    {
        var list = folders.Select(f => Path.TrimEndingDirectorySeparator(Path.GetFullPath(f)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var tracks = await Task.Run(() => list.SelectMany(folder => Scan(folder, cancel)).ToList(), cancel);
        var next = new Snapshot(list, tracks);
        Set(next, save: true);
        return next;
    }

    static IEnumerable<Track> Scan(string folder, CancellationToken cancel)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                // A junction back up the tree would otherwise scan forever.
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            }).Where(IsAudio).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break; // folder gone (unplugged drive): its tracks drop out until it is back
        }
        foreach (var path in files)
        {
            cancel.ThrowIfCancellationRequested();
            long size;
            try { size = new FileInfo(path).Length; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            var t = ShellTags.Read(path);
            yield return new Track(IdOf(path), folder, path, t.Title ?? Path.GetFileNameWithoutExtension(path), t.Artist,
                t.Album, t.AlbumArtist, t.Track, t.Year, t.DurationSec, size,
                CodecOf(path), t.BitrateKbps, t.SampleRateHz, t.BitDepth, t.Channels);
        }
    }

    void Set(Snapshot next, bool save)
    {
        lock (gate)
        {
            snapshot = next;
            byId = next.Tracks.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
        }
        if (!save) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(next));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Next start shows an empty library until a rescan; nothing is lost but the cache.
        }
    }

    // Answers /local/<id>, with byte ranges: Chromium seeks an <audio> element by asking for them.
    public CoreWebView2WebResourceResponse Serve(CoreWebView2Environment env, Uri uri, string? rangeHeader)
    {
        var id = uri.AbsolutePath[UrlPrefix.Length..];
        Track? track;
        lock (gate) byId.TryGetValue(id, out track);
        if (track == null) return env.CreateWebResourceResponse(null, 404, "Not Found", "");
        FileStream stream;
        try
        {
            stream = new FileStream(track.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return env.CreateWebResourceResponse(null, 404, "Not Found", "");
        }
        var length = stream.Length;
        var type = Types.TryGetValue(Path.GetExtension(track.Path), out var t) ? t : "application/octet-stream";
        var common = $"Content-Type: {type}\nAccept-Ranges: bytes\nX-Content-Type-Options: nosniff";
        if (string.IsNullOrWhiteSpace(rangeHeader))
            return env.CreateWebResourceResponse(stream, 200, "OK", $"{common}\nContent-Length: {length}");
        if (ParseRange(rangeHeader, length) is not var (start, end))
        {
            stream.Dispose();
            return env.CreateWebResourceResponse(null, 416, "Range Not Satisfiable", $"Content-Range: bytes */{length}");
        }
        stream.Position = start;
        return env.CreateWebResourceResponse(new RangeStream(stream, end - start + 1), 206, "Partial Content",
            $"{common}\nContent-Range: bytes {start}-{end}/{length}\nContent-Length: {end - start + 1}");
    }

    // "bytes=a-b", "bytes=a-" or "bytes=-n" as an inclusive [start, end]; null when unsatisfiable or not one range.
    public static (long Start, long End)? ParseRange(string header, long length)
    {
        header = header.Trim();
        if (length <= 0 || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return null;
        var spec = header[6..].Trim();
        if (spec.Contains(',')) return null;
        var dash = spec.IndexOf('-');
        if (dash < 0) return null;
        var a = spec[..dash].Trim();
        var b = spec[(dash + 1)..].Trim();
        if (a.Length == 0)
        {
            if (!long.TryParse(b, out var suffix) || suffix <= 0) return null;
            return (Math.Max(0, length - suffix), length - 1);
        }
        if (!long.TryParse(a, out var start) || start < 0 || start >= length) return null;
        if (b.Length == 0) return (start, length - 1);
        if (!long.TryParse(b, out var end) || end < start) return null;
        return (start, Math.Min(end, length - 1));
    }

    // The [count] bytes from the stream's current position, then end of stream.
    sealed class RangeStream(Stream inner, long length) : Stream
    {
        readonly long count = length;
        long left = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => count;
        public override long Position { get => count - left; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int size)
        {
            if (left <= 0) return 0;
            var n = inner.Read(buffer, offset, (int)Math.Min(size, left));
            left -= n;
            return n;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int size) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
