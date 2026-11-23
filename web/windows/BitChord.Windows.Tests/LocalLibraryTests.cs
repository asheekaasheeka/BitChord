using BitChord.Windows.Local;
using Xunit;

namespace BitChord.Windows.Tests;

public sealed class LocalLibraryTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "bitchord-local-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("bytes=0-", 0, 99)]
    [InlineData("bytes=10-19", 10, 19)]
    [InlineData("bytes=90-500", 90, 99)]
    [InlineData("bytes=-10", 90, 99)]
    [InlineData("bytes=-500", 0, 99)]
    [InlineData(" BYTES=5-5 ", 5, 5)]
    public void ParsesRanges(string header, long start, long end) =>
        Assert.Equal((start, end), LocalLibrary.ParseRange(header, 100));

    [Theory]
    [InlineData("bytes=100-")]
    [InlineData("bytes=20-10")]
    [InlineData("bytes=0-1,5-6")]
    [InlineData("bytes=-0")]
    [InlineData("items=0-1")]
    [InlineData("bytes=a-b")]
    public void RefusesBadRanges(string header) => Assert.Null(LocalLibrary.ParseRange(header, 100));

    [Fact]
    public async Task ScansAudioOnlyAndKeepsTheCache()
    {
        var music = Path.Combine(dir, "music");
        Directory.CreateDirectory(Path.Combine(music, "album"));
        File.WriteAllBytes(Path.Combine(music, "album", "01 Song.mp3"), new byte[64]);
        File.WriteAllBytes(Path.Combine(music, "cover.jpg"), new byte[8]);
        File.WriteAllText(Path.Combine(music, "notes.txt"), "");
        var cache = Path.Combine(dir, "library.json");

        var scanned = await new LocalLibrary(cache).ScanAsync([music, music + Path.DirectorySeparatorChar]);
        var track = Assert.Single(scanned.Tracks);
        Assert.Single(scanned.Folders); // the same folder twice is one folder
        Assert.Equal("01 Song", track.Title); // no tags: the file name
        Assert.Equal(64, track.Size);
        Assert.Equal(LocalLibrary.IdOf(Path.Combine(music, "album", "01 Song.mp3")), track.Id);

        var reloaded = new LocalLibrary(cache).Current;
        Assert.Equal(track.Id, Assert.Single(reloaded.Tracks).Id);
    }

    [Fact]
    public void ReadsTheAudioFormatThroughWindows()
    {
        // A WAV every Windows install has; skipped where it doesn't.
        var wav = Directory.EnumerateFiles(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\Media", "*.wav").FirstOrDefault();
        if (wav == null) return;
        var tags = ShellTags.Read(wav);
        Assert.True(tags.SampleRateHz is >= 8000 and <= 384_000, $"sample rate {tags.SampleRateHz}");
        Assert.True(tags.BitDepth is 8 or 16 or 24 or 32, $"bit depth {tags.BitDepth}");
        Assert.True(tags.Channels is 1 or 2, $"channels {tags.Channels}");
        Assert.True(tags.DurationSec > 0);
    }

    [Fact]
    public void IdsIgnorePathCase() =>
        Assert.Equal(LocalLibrary.IdOf(@"C:\Music\A.flac"), LocalLibrary.IdOf(@"c:\music\a.FLAC"));
}
