using System.Text;

namespace BitChord.Windows.Local;

// The picture inside an audio file's own tags: ID3v2 APIC/PIC (MP3), a FLAC PICTURE block, or an MP4 covr
// atom (M4A). Read here rather than through Windows' thumbnail handler, which fails on ID3v2.4 art
// (WTS_E_FAILEDEXTRACTION, measured on files whose APIC frame is intact). Front cover first, else the first picture.
public static class EmbeddedArt
{
    public sealed record Picture(byte[] Bytes, string ContentType);

    const int MaxTagBytes = 32 * 1024 * 1024;

    public static Picture? Read(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Read(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static Picture? Read(Stream stream)
    {
        var head = new byte[12];
        if (stream.Read(head, 0, 12) < 12) return null;
        stream.Position = 0;
        try
        {
            if (head[0] == 'I' && head[1] == 'D' && head[2] == '3') return Id3(stream);
            if (head[0] == 'f' && head[1] == 'L' && head[2] == 'a' && head[3] == 'C') return Flac(stream);
            if (head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p') return Mp4(stream);
        }
        catch (Exception e) when (e is EndOfStreamException or ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            // A damaged tag is a file without art, never a failed play.
        }
        return null;
    }

    // ── ID3v2 ────────────────────────────────────────────────────────────

    static Picture? Id3(Stream s)
    {
        var header = ReadExactly(s, 10);
        int version = header[3];
        var tagUnsync = (header[5] & 0x80) != 0;
        var size = SyncSafe(header, 6);
        if (size <= 0 || size > MaxTagBytes || version is < 2 or > 4) return null;
        var tag = ReadExactly(s, size);
        // v2.2 and v2.3 unsynchronise the whole tag; v2.4 does it per frame.
        if (tagUnsync && version < 4) tag = Unsync(tag);
        var at = 0;
        if (version >= 3 && (header[5] & 0x40) != 0)
            at += version == 4 ? SyncSafe(tag, 0) : BigEndian(tag, 0, 4) + 4; // skip the extended header

        Picture? first = null;
        var idLength = version == 2 ? 3 : 4;
        var headerLength = version == 2 ? 6 : 10;
        while (at + headerLength <= tag.Length && tag[at] != 0)
        {
            var id = Encoding.ASCII.GetString(tag, at, idLength);
            var frameSize = version == 2 ? BigEndian(tag, at + 3, 3) : version == 4 ? SyncSafe(tag, at + 4) : BigEndian(tag, at + 4, 4);
            var flags = version == 2 ? 0 : (tag[at + 8] << 8) | tag[at + 9];
            var body = at + headerLength;
            if (frameSize <= 0 || body + frameSize > tag.Length) break;
            if (id is "APIC" or "PIC")
            {
                var data = tag.AsSpan(body, frameSize).ToArray();
                // v2.4 frame flags: 0x0C compressed/encrypted (skip), 0x02 unsynchronised, 0x01 data-length prefix.
                if (version == 4 && (flags & 0x0C) == 0)
                {
                    if ((flags & 0x02) != 0) data = Unsync(data);
                    if ((flags & 0x01) != 0) data = data[4..];
                }
                if (version != 4 || (flags & 0x0C) == 0)
                {
                    var (pic, type) = id == "PIC" ? ParsePic(data) : ParseApic(data);
                    if (pic != null)
                    {
                        if (type == 3) return pic;
                        first ??= pic;
                    }
                }
            }
            at = body + frameSize;
        }
        return first;
    }

    // encoding, MIME (Latin-1, NUL-ended), picture type, description (NUL-ended in that encoding), data.
    static (Picture?, int) ParseApic(byte[] d)
    {
        var encoding = d[0];
        var at = Array.IndexOf(d, (byte)0, 1);
        if (at < 0) return (null, 0);
        var type = d[at + 1];
        return (Image(d, SkipText(d, at + 2, encoding)), type);
    }

    // v2.2: encoding, a 3-letter format ("JPG", "PNG"), picture type, description, data.
    static (Picture?, int) ParsePic(byte[] d) => (Image(d, SkipText(d, 5, d[0])), d[4]);

    static int SkipText(byte[] d, int at, byte encoding)
    {
        if (encoding is 1 or 2) // UTF-16: ends at an aligned 00 00
        {
            while (at + 1 < d.Length && (d[at] != 0 || d[at + 1] != 0)) at += 2;
            return at + 2;
        }
        while (at < d.Length && d[at] != 0) at++;
        return at + 1;
    }

    static byte[] Unsync(byte[] data)
    {
        var output = new List<byte>(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            output.Add(data[i]);
            if (data[i] == 0xFF && i + 1 < data.Length && data[i + 1] == 0) i++;
        }
        return [.. output];
    }

    // ── FLAC ─────────────────────────────────────────────────────────────

    static Picture? Flac(Stream s)
    {
        s.Position = 4;
        Picture? first = null;
        while (true)
        {
            var header = ReadExactly(s, 4);
            var last = (header[0] & 0x80) != 0;
            var type = header[0] & 0x7F;
            var length = BigEndian(header, 1, 3);
            if (type == 6 && length <= MaxTagBytes)
            {
                var b = ReadExactly(s, length);
                var pictureType = BigEndian(b, 0, 4);
                var mimeLength = BigEndian(b, 4, 4);
                var at = 8 + mimeLength;
                at += 4 + BigEndian(b, at, 4); // description
                at += 16; // width, height, depth, colours
                var dataLength = BigEndian(b, at, 4);
                var pic = Image(b.AsSpan(at + 4, dataLength).ToArray(), 0);
                if (pic != null && pictureType == 3) return pic;
                first ??= pic;
            }
            else s.Seek(length, SeekOrigin.Current);
            if (last) return first;
        }
    }

    // ── MP4 ──────────────────────────────────────────────────────────────

    static Picture? Mp4(Stream s)
    {
        var data = FindAtom(s, 0, s.Length, ["moov", "udta", "meta", "ilst", "covr", "data"]);
        if (data is not var (start, length) || length < 8) return null;
        s.Position = start + 8; // type indicator and locale
        return Image(ReadExactly(s, (int)Math.Min(length - 8, MaxTagBytes)), 0);
    }

    // The body (start, length) of the atom at the end of [path], searched from [from] to [to].
    static (long, long)? FindAtom(Stream s, long from, long to, string[] path)
    {
        var at = from;
        while (at + 8 <= to)
        {
            s.Position = at;
            var h = ReadExactly(s, 8);
            long size = (uint)BigEndian(h, 0, 4);
            var name = Encoding.ASCII.GetString(h, 4, 4);
            var body = at + 8;
            if (size == 1) { size = BigEndian64(ReadExactly(s, 8)); body += 8; }
            else if (size == 0) size = to - at;
            if (size < 8 || at + size > to) return null;
            if (name == path[0])
            {
                var childStart = name == "meta" ? body + 4 : body; // meta is a full box: version and flags first
                return path.Length == 1 ? (body, at + size - body) : FindAtom(s, childStart, at + size, path[1..]);
            }
            at += size;
        }
        return null;
    }

    // ── Shared ───────────────────────────────────────────────────────────

    // The image type comes from its bytes, not the tag's MIME: taggers write "image/jpg", "JPG", or nothing.
    // Looks a few bytes past [at]: some taggers pad the description with extra NULs (measured: one extra
    // 00 before the PNG in real ID3v2.4 files), which would leave a strict offset one byte short.
    static Picture? Image(byte[] d, int at)
    {
        for (var i = Math.Max(0, at); i < d.Length && i <= at + 16; i++)
        {
            var type = TypeAt(d.AsSpan(i));
            if (type != null) return new Picture(i == 0 ? d : d[i..], type);
        }
        return null;
    }

    static string? TypeAt(ReadOnlySpan<byte> b) => b switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0x47, 0x49, 0x46, 0x38, ..] => "image/gif",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };

    static byte[] ReadExactly(Stream s, int count)
    {
        var buffer = new byte[count];
        s.ReadExactly(buffer);
        return buffer;
    }

    static int SyncSafe(byte[] b, int at) => (b[at] & 0x7F) << 21 | (b[at + 1] & 0x7F) << 14 | (b[at + 2] & 0x7F) << 7 | (b[at + 3] & 0x7F);

    static int BigEndian(byte[] b, int at, int count)
    {
        var v = 0;
        for (var i = 0; i < count; i++) v = checked(v << 8 | b[at + i]);
        return v;
    }

    static long BigEndian64(byte[] b)
    {
        long v = 0;
        foreach (var x in b) v = v << 8 | x;
        return v;
    }
}
