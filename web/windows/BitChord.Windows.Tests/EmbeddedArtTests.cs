using System.Text;
using BitChord.Windows.Local;
using Xunit;

namespace BitChord.Windows.Tests;

public sealed class EmbeddedArtTests
{
    static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 0xFF, 0xD9];
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9];

    static byte[] SyncSafe(int v) => [(byte)(v >> 21 & 0x7F), (byte)(v >> 14 & 0x7F), (byte)(v >> 7 & 0x7F), (byte)(v & 0x7F)];
    static byte[] Be(int v, int n) => Enumerable.Range(0, n).Select(i => (byte)(v >> (8 * (n - 1 - i)))).ToArray();

    static byte[] Id3(int version, params (byte Type, byte[] Image, bool Utf16)[] pictures)
    {
        var frames = new List<byte>();
        foreach (var (type, image, utf16) in pictures)
        {
            var body = new List<byte> { (byte)(utf16 ? 1 : 0) };
            body.AddRange(Encoding.Latin1.GetBytes("image/jpg"));
            body.Add(0);
            body.Add(type);
            body.AddRange(utf16 ? [0xFF, 0xFE, (byte)'c', 0, 0, 0] : [(byte)'c', 0]);
            body.AddRange(image);
            frames.AddRange(Encoding.ASCII.GetBytes("APIC"));
            frames.AddRange(version == 4 ? SyncSafe(body.Count) : Be(body.Count, 4));
            frames.AddRange([0, 0]);
            frames.AddRange(body);
        }
        frames.AddRange(new byte[16]); // padding
        return [.. "ID3"u8.ToArray(), (byte)version, 0, 0, .. SyncSafe(frames.Count), .. frames, 0xFF, 0xFB];
    }

    static EmbeddedArt.Picture? Read(byte[] file) => EmbeddedArt.Read(new MemoryStream(file));

    [Fact]
    public void ReadsId3v23()
    {
        var pic = Read(Id3(3, (3, Jpeg, false)));
        Assert.Equal("image/jpeg", pic?.ContentType);
        Assert.Equal(Jpeg, pic!.Bytes);
    }

    [Fact]
    public void ReadsId3v24WithAUtf16DescriptionAndPrefersTheFrontCover()
    {
        var pic = Read(Id3(4, (0, Png, true), (3, Jpeg, true)));
        Assert.Equal(Jpeg, pic!.Bytes);
        Assert.Equal(Png, Read(Id3(4, (0, Png, true)))!.Bytes); // no front cover: the first picture
    }

    [Fact]
    public void ToleratesAnExtraNulBeforeThePicture()
    {
        // Measured in real files: APIC "\0image/png\0" type 0, "\0" description, then one more 00 before the PNG.
        byte[] png = [0x00, .. Png];
        Assert.Equal(Png, Read(Id3(4, (0, png, false)))!.Bytes);
    }

    [Fact]
    public void ReadsAFlacPictureBlock()
    {
        var mime = Encoding.ASCII.GetBytes("image/png");
        byte[] picture = [.. Be(3, 4), .. Be(mime.Length, 4), .. mime, .. Be(0, 4), .. new byte[16], .. Be(Png.Length, 4), .. Png];
        byte[] streamInfo = [0x00, .. Be(34, 3), .. new byte[34]];
        byte[] file = [.. "fLaC"u8.ToArray(), .. streamInfo, 0x86, .. Be(picture.Length, 3), .. picture, 0xFF, 0xF8];
        Assert.Equal(Png, Read(file)!.Bytes);
    }

    [Fact]
    public void ReadsAnMp4CoverAtom()
    {
        static byte[] Atom(string name, params byte[][] children)
        {
            var body = children.SelectMany(c => c).ToArray();
            return [.. Be(body.Length + 8, 4), .. Encoding.ASCII.GetBytes(name), .. body];
        }
        var data = Atom("data", [0, 0, 0, 13, 0, 0, 0, 0], Jpeg);
        var meta = Atom("meta", [0, 0, 0, 0], Atom("hdlr", new byte[25]), Atom("ilst", Atom("covr", data)));
        byte[] file = [.. Atom("ftyp", "M4A "u8.ToArray()), .. Atom("moov", Atom("udta", meta)), .. Atom("mdat", new byte[4])];
        Assert.Equal(Jpeg, Read(file)!.Bytes);
    }

    [Fact]
    public void NoArtIsNull()
    {
        Assert.Null(Read(Id3(3)));
        Assert.Null(Read([0xFF, 0xFB, 0x90, 0x00, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Null(Read([.. "ID3"u8.ToArray(), 3, 0, 0, 0x7F, 0x7F, 0x7F, 0x7F, 0, 0])); // tag size past the file
    }
}
