using BitChord.Windows.Karaoke;
using Xunit;

public class KaraokeDspTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(5120)]
    [InlineData(7680)]
    public void FftMatchesNaiveDftAndInverts(int n)
    {
        var rng = new Random(n);
        var re = Enumerable.Range(0, n).Select(_ => rng.NextDouble() - 0.5).ToArray();
        var im = Enumerable.Range(0, n).Select(_ => rng.NextDouble() - 0.5).ToArray();
        var (r0, i0) = ((double[])re.Clone(), (double[])im.Clone());
        var fft = new Fft(n);
        fft.Forward(re, im);
        foreach (var k in new[] { 0, 1, n / 3, n - 1 })
        {
            double sr = 0, si = 0;
            for (var j = 0; j < n; j++)
            {
                var a = -2 * Math.PI * j * k / n;
                sr += r0[j] * Math.Cos(a) - i0[j] * Math.Sin(a);
                si += r0[j] * Math.Sin(a) + i0[j] * Math.Cos(a);
            }
            Assert.Equal(sr, re[k], 1e-8);
            Assert.Equal(si, im[k], 1e-8);
        }
        fft.Inverse(re, im);
        for (var j = 0; j < n; j++)
        {
            Assert.Equal(r0[j], re[j], 1e-10);
            Assert.Equal(i0[j], im[j], 1e-10);
        }
    }

    [Fact]
    public void IdentityModelGivesBackTheMixTimesCompensate()
    {
        // A pass-through "model" must return the input (scaled), across chunk edges and per channel.
        var model = MdxModel.Kara2;
        var n = 300_000; // more than one chunk, not a multiple of it
        var mix = new float[2 * n];
        for (var i = 0; i < n; i++)
        {
            mix[2 * i] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / 44100));
            mix[2 * i + 1] = (float)(0.3 * Math.Sin(2 * Math.PI * 1000 * i / 44100));
        }
        var chunks = 0;
        var output = new MdxSeparator(model).Separate(mix, x => { chunks++; return x; });
        Assert.Equal(2, chunks);
        Assert.Equal(mix.Length, output.Length);
        // The model only sees bins below DimF (~17.6 kHz), so the hard start and end of the sines ring a little.
        // Exactness against torch.stft/istft was checked separately (130 dB SNR on a real song).
        for (var i = 0; i < mix.Length; i++)
        {
            var edge = i < 2 * 10_000 || i >= mix.Length - 2 * 10_000;
            Assert.Equal(mix[i] * model.Compensate, output[i], edge ? 1e-1f : 1e-5f);
        }
    }

    [Fact]
    public void RejectsOddSampleCount() =>
        Assert.Throws<ArgumentException>(() => new MdxSeparator(MdxModel.Kara2).Separate(new float[3], x => x));
}
