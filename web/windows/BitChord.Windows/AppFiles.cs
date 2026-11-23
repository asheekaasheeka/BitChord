namespace BitChord.Windows;

public static class AppFiles
{
    // The existing file under root that the URL path names, or null: client routes (/album/1), directories,
    // and paths that resolve outside root (.., %5C, drive letters) all give null.
    public static string? Find(string root, Uri uri)
    {
        try
        {
            var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(rootFull, Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/', '\\')));
            return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? full : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or InvalidOperationException)
        {
            return null;
        }
    }

    // Chromium is strict about module scripts, CSS and streamed wasm, so these must be right; the rest is octet-stream.
    static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json",
        [".map"] = "application/json",
        [".webmanifest"] = "application/manifest+json",
        [".wasm"] = "application/wasm",
        [".onnx"] = "application/octet-stream",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".ico"] = "image/x-icon",
        [".woff2"] = "font/woff2",
        [".woff"] = "font/woff",
        [".ttf"] = "font/ttf",
        [".txt"] = "text/plain; charset=utf-8",
    };

    public static string ContentType(string path) =>
        Types.TryGetValue(Path.GetExtension(path), out var type) ? type : "application/octet-stream";
}
