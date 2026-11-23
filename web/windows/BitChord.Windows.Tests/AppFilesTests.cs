using BitChord.Windows;
using Xunit;

namespace BitChord.Windows.Tests;

public sealed class AppFilesTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "bitchord-appfiles-" + Guid.NewGuid().ToString("N"));
    readonly string root;

    public AppFilesTests()
    {
        root = Path.Combine(dir, "web");
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        File.WriteAllText(Path.Combine(root, "index.html"), "");
        File.WriteAllText(Path.Combine(root, "assets", "app.js"), "");
        File.WriteAllText(Path.Combine(root, "assets", "a b.js"), "");
        File.WriteAllText(Path.Combine(dir, "secret.txt"), ""); // beside root: must never be reachable
    }

    public void Dispose() => Directory.Delete(dir, recursive: true);

    string? Find(string url) => AppFiles.Find(root, new Uri(url));

    [Theory]
    [InlineData("https://app.bitchord.example/index.html", "index.html")]
    [InlineData("https://app.bitchord.example/assets/app.js", "assets/app.js")]
    [InlineData("https://app.bitchord.example/assets/app.js?v=1#x", "assets/app.js")]
    [InlineData("https://app.bitchord.example/assets/a%20b.js", "assets/a b.js")]
    [InlineData("https://app.bitchord.example/ASSETS/APP.JS", "assets/app.js")]
    public void ExistingFileIsFound(string url, string file) =>
        Assert.Equal(Path.GetFullPath(Path.Combine(root, file)), Find(url), ignoreCase: true);

    [Theory]
    [InlineData("https://app.bitchord.example/")]
    [InlineData("https://app.bitchord.example/home")]
    [InlineData("https://app.bitchord.example/album/abc123")]
    [InlineData("https://app.bitchord.example/assets")]
    [InlineData("https://app.bitchord.example/assets/missing.js")]
    public void RouteOrMissingIsNull(string url) => Assert.Null(Find(url));

    [Theory]
    [InlineData("https://app.bitchord.example/../secret.txt")]
    [InlineData("https://app.bitchord.example/%2e%2e/secret.txt")]
    [InlineData("https://app.bitchord.example/..%2Fsecret.txt")]
    [InlineData("https://app.bitchord.example/..%5Csecret.txt")]
    [InlineData("https://app.bitchord.example/assets/..%5C..%5Csecret.txt")]
    [InlineData("https://app.bitchord.example/%2F%2F..%2Fsecret.txt")]
    public void TraversalIsNull(string url) => Assert.Null(Find(url));

    [Theory]
    [InlineData("index.html", "text/html; charset=utf-8")]
    [InlineData("assets/index-abc.js", "text/javascript; charset=utf-8")]
    [InlineData("a.MJS", "text/javascript; charset=utf-8")]
    [InlineData("a.css", "text/css; charset=utf-8")]
    [InlineData("ort.wasm", "application/wasm")]
    [InlineData("models/beat.onnx", "application/octet-stream")]
    [InlineData("favicon.svg", "image/svg+xml")]
    [InlineData("font.woff2", "font/woff2")]
    [InlineData("data.json", "application/json")]
    [InlineData("noext", "application/octet-stream")]
    public void ContentTypeByExtension(string path, string type) => Assert.Equal(type, AppFiles.ContentType(path));

    [Fact]
    public void AbsolutePathOutsideRootIsNull() =>
        Assert.Null(Find("https://app.bitchord.example/" + Uri.EscapeDataString(Path.Combine(dir, "secret.txt"))));
}
