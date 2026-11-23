using System.Security.Cryptography;

namespace BitChord.Windows.Karaoke;

// Models are not bundled (their licences are unclear): downloaded once, after the user agrees, and
// used only when their SHA-256 matches.
public static class ModelStore
{
    static readonly HashSet<string> Verified = [];

    // Returns the model's path, or null when the user declined the download.
    public static async Task<string?> EnsureAsync(string folder, MdxModel model, Func<bool> askToDownload,
        IProgress<double>? progress, CancellationToken ct)
    {
        var path = Path.Combine(folder, model.FileName);
        if (Verified.Contains(path)) return path;
        if (File.Exists(path) && await HashMatchesAsync(path, model.Sha256, ct))
        {
            Verified.Add(path);
            return path;
        }
        if (!askToDownload()) return null;

        Directory.CreateDirectory(folder);
        var part = path + ".part";
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            using (var response = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var src = await response.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(part);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    done += read;
                    if (done > model.Bytes) throw new InvalidDataException("The model download is larger than expected.");
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    progress?.Report(done / (double)model.Bytes);
                }
            }
            if (!await HashMatchesAsync(part, model.Sha256, ct))
                throw new InvalidDataException("The downloaded model did not match its checksum.");
            File.Move(part, path, overwrite: true);
            Verified.Add(path);
            return path;
        }
        finally
        {
            if (File.Exists(part)) File.Delete(part);
        }
    }

    public static async Task<bool> HashMatchesAsync(string path, string sha256, CancellationToken ct)
    {
        await using var file = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(file, ct);
        return Convert.ToHexString(hash).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }
}
