using System.Text.Json;
using System.Text.RegularExpressions;

namespace BitChord.Windows.Karaoke;

// One message from the web player. Anything that does not parse into exactly this is dropped.
public sealed record KaraokeMessage(string Type, string Job, int Frames)
{
    public const int SampleRate = 44_100;
    // 10 minutes of stereo float32 is a 212 MB shared buffer; longer tracks are not offered karaoke.
    public const int MaxFrames = SampleRate * 60 * 10;

    static readonly Regex JobId = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);
    static readonly string[] Types = ["separate", "run", "cancel", "release"];

    public static bool TryParse(string json, out KaraokeMessage message)
    {
        message = null!;
        if (json.Length > 1024) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("karaoke", out var t) || t.ValueKind != JsonValueKind.String) return false;
            if (!root.TryGetProperty("job", out var j) || j.ValueKind != JsonValueKind.String) return false;
            var type = t.GetString()!;
            var job = j.GetString()!;
            if (!Types.Contains(type) || !JobId.IsMatch(job)) return false;
            var frames = 0;
            if (type == "separate")
            {
                if (!root.TryGetProperty("frames", out var f) || !f.TryGetInt32(out frames)) return false;
                if (frames < 1 || frames > MaxFrames) return false;
            }
            message = new KaraokeMessage(type, job, frames);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
