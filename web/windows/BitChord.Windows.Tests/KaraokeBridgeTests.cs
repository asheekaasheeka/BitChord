using BitChord.Windows.Karaoke;
using Xunit;

public class KaraokeMessageTests
{
    [Theory]
    [InlineData("""{"karaoke":"separate","job":"a1_-","frames":44100}""", "separate", 44100)]
    [InlineData("""{"karaoke":"run","job":"x"}""", "run", 0)]
    [InlineData("""{"karaoke":"release","job":"x","frames":"ignored"}""", "release", 0)]
    public void Accepts(string json, string type, int frames)
    {
        Assert.True(KaraokeMessage.TryParse(json, out var m));
        Assert.Equal(type, m.Type);
        Assert.Equal(frames, m.Frames);
    }

    [Theory]
    [InlineData("""{"karaoke":"separate","job":"x"}""")] // no frames
    [InlineData("""{"karaoke":"separate","job":"x","frames":0}""")]
    [InlineData("""{"karaoke":"separate","job":"x","frames":26460001}""")] // over 10 minutes
    [InlineData("""{"karaoke":"separate","job":"x","frames":1.5}""")]
    [InlineData("""{"karaoke":"separate","job":"..\\x","frames":1}""")]
    [InlineData("""{"karaoke":"delete","job":"x"}""")]
    [InlineData("""{"job":"x"}""")]
    [InlineData("""["karaoke"]""")]
    [InlineData("\"separate\"")]
    [InlineData("not json")]
    public void Rejects(string json) => Assert.False(KaraokeMessage.TryParse(json, out _));

    [Fact]
    public void RejectsLongMessages() =>
        Assert.False(KaraokeMessage.TryParse($$"""{"karaoke":"run","job":"x","pad":"{{new string('a', 2000)}}"}""", out _));
}

public class ModelStoreTests
{
    static readonly MdxModel Tiny = MdxModel.Kara2 with
    {
        FileName = "tiny.onnx",
        Sha256 = "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824", // "hello"
    };

    [Fact]
    public async Task UsesAVerifiedFileWithoutAsking()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "tiny.onnx"), "hello");
        var path = await ModelStore.EnsureAsync(dir, Tiny, () => throw new Exception("asked"), null, default);
        Assert.Equal(Path.Combine(dir, "tiny.onnx"), path);
    }

    [Fact]
    public async Task AsksWhenTheFileIsWrongAndStopsWhenDeclined()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "tiny.onnx"), "tampered");
        var asked = false;
        var path = await ModelStore.EnsureAsync(dir, Tiny, () => { asked = true; return false; }, null, default);
        Assert.True(asked);
        Assert.Null(path);
    }
}
