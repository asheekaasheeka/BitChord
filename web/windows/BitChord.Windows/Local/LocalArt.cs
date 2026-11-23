using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BitChord.Windows.Local;

// Album art for a local file: the picture in its tags (EmbeddedArt), else what Windows' thumbnail handler
// extracts (formats EmbeddedArt doesn't read), else a cover image beside it. Cached on disk as JPEG, keyed by file id and size.
sealed class LocalArt : IDisposable
{
    public sealed record Image(byte[] Bytes, string ContentType);

    const int Px = 600;
    static readonly string[] CoverNames = ["cover", "folder", "front", "album", "albumart"];
    static readonly (string Ext, string Type)[] CoverTypes = [(".jpg", "image/jpeg"), (".jpeg", "image/jpeg"), (".png", "image/png"), (".webp", "image/webp")];

    readonly string cacheFolder;
    // Shell thumbnail handlers are COM objects that expect an STA; one thread runs them all, in order.
    readonly BlockingCollection<Action> queue = new();
    readonly Thread sta;

    public LocalArt(string cacheFolder)
    {
        this.cacheFolder = cacheFolder;
        sta = new Thread(() =>
        {
            foreach (var work in queue.GetConsumingEnumerable()) work();
        }) { IsBackground = true, Name = "BitChord local art" };
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
    }

    public async Task<Image?> GetAsync(LocalLibrary.Track track)
    {
        // The file's own tags first: quick, and right where Windows' handler fails (ID3v2.4).
        if (await Task.Run(() => EmbeddedArt.Read(track.Path)) is { } embedded) return new Image(embedded.Bytes, embedded.ContentType);
        var cached = Path.Combine(cacheFolder, $"{track.Id}-{track.Size}.jpg");
        var none = Path.ChangeExtension(cached, ".none");
        if (File.Exists(cached)) return new Image(await File.ReadAllBytesAsync(cached), "image/jpeg");
        if (!File.Exists(none))
        {
            var bytes = await OnSta(() => Extract(track.Path));
            try
            {
                Directory.CreateDirectory(cacheFolder);
                if (bytes != null) await File.WriteAllBytesAsync(cached, bytes);
                else await File.WriteAllTextAsync(none, ""); // no embedded art: don't ask Windows again
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Uncached: extracted again next time.
            }
            if (bytes != null) return new Image(bytes, "image/jpeg");
        }
        return await CoverBeside(track.Path);
    }

    static async Task<Image?> CoverBeside(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir == null) return null;
        foreach (var name in CoverNames)
            foreach (var (ext, type) in CoverTypes)
            {
                var file = Path.Combine(dir, name + ext);
                try
                {
                    if (File.Exists(file)) return new Image(await File.ReadAllBytesAsync(file), type);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        return null;
    }

    Task<T> OnSta<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Add(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception e) { done.SetException(e); }
        });
        return done.Task;
    }

    // The file's embedded picture as JPEG, or null. THUMBNAILONLY: no generic file-type icon in its place.
    static byte[]? Extract(string path)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) != 0 || factory == null) return null;
        try
        {
            if (factory.GetImage(new Size(Px, Px), ThumbnailOnly | BiggerSizeOk, out var hbitmap) != 0 || hbitmap == IntPtr.Zero) return null;
            try
            {
                using var bitmap = System.Drawing.Image.FromHbitmap(hbitmap);
                using var output = new MemoryStream();
                bitmap.Save(output, ImageFormat.Jpeg);
                return output.ToArray();
            }
            finally
            {
                DeleteObject(hbitmap);
            }
        }
        catch (Exception e) when (e is COMException or ExternalException or ArgumentException)
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    public void Dispose() => queue.CompleteAdding();

    const int BiggerSizeOk = 0x1;
    const int ThumbnailOnly = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    readonly struct Size(int cx, int cy)
    {
        public readonly int Cx = cx;
        public readonly int Cy = cy;
    }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(Size size, int flags, out IntPtr hbitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr bindCtx, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? item);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr handle);
}
