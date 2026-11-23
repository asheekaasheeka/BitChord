using System.Runtime.InteropServices;

namespace BitChord.Windows.Local;

// A file's music tags through the Windows property system (the same ones Explorer's Details pane shows),
// so no tag-reading library is bundled. Covers whatever formats Windows has a property handler for.
public static class ShellTags
{
    public sealed record Tags(string? Title, string? Artist, string? Album, string? AlbumArtist, int? Track, int? Year, double? DurationSec,
        int? BitrateKbps = null, int? SampleRateHz = null, int? BitDepth = null, int? Channels = null);

    static readonly Guid Summary = new("F29F85E0-4FF9-1068-AB91-08002B27B3D9");
    static readonly Guid Music = new("56A3372E-CE9C-11D2-9F0E-006097C686F6");
    // FMTID_AudioSummaryInformation: 3 duration, 4 bitrate (bit/s), 5 sample rate, 6 sample size (bits), 7 channels.
    static readonly Guid Media = new("64440490-4C8B-11D1-8B70-080036B11A03");

    public static Tags Read(string path)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, GpsBestEffort, ref iid, out var store) != 0 || store == null)
            return new Tags(null, null, null, null, null, null, null);
        try
        {
            var duration = UInt64(store, Media, 3); // 100 ns units
            return new Tags(
                Text(store, Summary, 2), Text(store, Music, 2), Text(store, Music, 4), Text(store, Music, 13),
                (int?)UInt64(store, Music, 7), (int?)UInt64(store, Music, 5), // PKEY_Media_Year shares the music FMTID
                duration is > 0 ? duration / 10_000_000.0 : null,
                UInt64(store, Media, 4) is { } bps ? (int)Math.Round(bps / 1000.0) : null,
                (int?)UInt64(store, Media, 5), (int?)UInt64(store, Media, 6), (int?)UInt64(store, Media, 7));
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    static string? Text(IPropertyStore store, Guid fmtid, uint pid)
    {
        var key = new PropertyKey(fmtid, pid);
        if (store.GetValue(ref key, out var pv) != 0) return null;
        try
        {
            if (pv.vt == 0 || PropVariantToStringAlloc(ref pv, out var p) != 0) return null;
            try
            {
                var s = Marshal.PtrToStringUni(p)?.Trim();
                return string.IsNullOrEmpty(s) ? null : s;
            }
            finally
            {
                Marshal.FreeCoTaskMem(p);
            }
        }
        finally
        {
            PropVariantClear(ref pv);
        }
    }

    static ulong? UInt64(IPropertyStore store, Guid fmtid, uint pid)
    {
        var key = new PropertyKey(fmtid, pid);
        if (store.GetValue(ref key, out var pv) != 0) return null;
        try
        {
            return pv.vt != 0 && PropVariantToUInt64(ref pv, out var v) == 0 && v > 0 ? v : null;
        }
        finally
        {
            PropVariantClear(ref pv);
        }
    }

    const int GpsBestEffort = 0x40;

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey(Guid fmtid, uint pid)
    {
        public Guid Fmtid = fmtid;
        public uint Pid = pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant
    {
        public ushort vt;
        ushort r1, r2, r3;
        IntPtr p1, p2;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bindCtx, int flags, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);

    [DllImport("propsys.dll")]
    static extern int PropVariantToStringAlloc(ref PropVariant pv, out IntPtr psz);

    [DllImport("propsys.dll")]
    static extern int PropVariantToUInt64(ref PropVariant pv, out ulong value);

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PropVariant pv);
}
