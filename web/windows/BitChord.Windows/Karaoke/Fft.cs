namespace BitChord.Windows.Karaoke;

// Complex FFT for n = 2^a * p (p odd): p radix-2 FFTs of size 2^a, then one direct p-point combine.
// Covers the MDX-Net sizes (5120 = 1024*5, 6144 = 2048*3, 7680 = 512*15) without a dependency.
public sealed class Fft
{
    readonly int n, m, p;
    readonly double[] cos, sin; // e^{-2πi k/n}
    readonly int[] rev;
    readonly double[] tmpRe, tmpIm;

    public int Size => n;

    public Fft(int n)
    {
        if (n < 1) throw new ArgumentOutOfRangeException(nameof(n));
        this.n = n;
        m = n & -n; // largest power of two dividing n
        p = n / m;
        cos = new double[n];
        sin = new double[n];
        for (var k = 0; k < n; k++)
        {
            cos[k] = Math.Cos(2 * Math.PI * k / n);
            sin[k] = -Math.Sin(2 * Math.PI * k / n);
        }
        rev = new int[m];
        var bits = System.Numerics.BitOperations.Log2((uint)m);
        for (var i = 0; i < m; i++) rev[i] = bits == 0 ? 0 : (int)(ReverseBits((uint)i) >> (32 - bits));
        tmpRe = new double[n];
        tmpIm = new double[n];
    }

    static uint ReverseBits(uint v)
    {
        v = ((v >> 1) & 0x55555555) | ((v & 0x55555555) << 1);
        v = ((v >> 2) & 0x33333333) | ((v & 0x33333333) << 2);
        v = ((v >> 4) & 0x0F0F0F0F) | ((v & 0x0F0F0F0F) << 4);
        v = ((v >> 8) & 0x00FF00FF) | ((v & 0x00FF00FF) << 8);
        return (v >> 16) | (v << 16);
    }

    // In place, unnormalised: X[k] = Σ x[j] e^{-2πi jk/n} (numpy/torch convention).
    public void Forward(double[] re, double[] im)
    {
        // Split into p interleaved sub-sequences, bit-reversed, each in its own block of m.
        for (var r = 0; r < p; r++)
            for (var j = 0; j < m; j++)
            {
                tmpRe[r * m + rev[j]] = re[r + p * j];
                tmpIm[r * m + rev[j]] = im[r + p * j];
            }
        var stride = n / m; // twiddle step for size-m FFTs in the size-n table
        for (var r = 0; r < p; r++)
        {
            var o = r * m;
            for (var len = 2; len <= m; len <<= 1)
            {
                var half = len >> 1;
                var step = stride * (m / len);
                for (var s = 0; s < m; s += len)
                    for (var j = 0; j < half; j++)
                    {
                        double wr = cos[j * step], wi = sin[j * step];
                        int a = o + s + j, b = a + half;
                        double xr = tmpRe[b] * wr - tmpIm[b] * wi, xi = tmpRe[b] * wi + tmpIm[b] * wr;
                        tmpRe[b] = tmpRe[a] - xr; tmpIm[b] = tmpIm[a] - xi;
                        tmpRe[a] += xr; tmpIm[a] += xi;
                    }
            }
        }
        if (p == 1)
        {
            Array.Copy(tmpRe, re, n);
            Array.Copy(tmpIm, im, n);
            return;
        }
        for (var k = 0; k < n; k++)
        {
            double sr = 0, si = 0;
            var km = k % m;
            for (var r = 0; r < p; r++)
            {
                var w = (int)((long)r * k % n);
                double fr = tmpRe[r * m + km], fi = tmpIm[r * m + km];
                sr += fr * cos[w] - fi * sin[w];
                si += fr * sin[w] + fi * cos[w];
            }
            re[k] = sr;
            im[k] = si;
        }
    }

    // In place, normalised by 1/n, so Inverse(Forward(x)) == x.
    public void Inverse(double[] re, double[] im)
    {
        for (var i = 0; i < n; i++) im[i] = -im[i];
        Forward(re, im);
        var s = 1.0 / n;
        for (var i = 0; i < n; i++)
        {
            re[i] *= s;
            im[i] = -im[i] * s;
        }
    }
}
