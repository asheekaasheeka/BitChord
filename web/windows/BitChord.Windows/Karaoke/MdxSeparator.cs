namespace BitChord.Windows.Karaoke;

// One UVR MDX-Net model. Numbers come from UVR's mdx_model_data/model_data.json, keyed by the model's hash.
public sealed record MdxModel(string FileName, string Sha256, long Bytes, int NFft, int Hop, int DimF, int DimT, float Compensate)
{
    // Removes the lead vocal, keeps backing vocals. Output stem = "Instrumental".
    public static readonly MdxModel Kara2 = new("UVR_MDXNET_KARA_2.onnx",
        "bf32e15105a09c0f7dddd2b67346146334d6f3ecb399ed7638eba2ab07cbf5f4", 52_786_726,
        NFft: 5120, Hop: 1024, DimF: 2048, DimT: 256, Compensate: 1.065f);

    public string Url => $"https://github.com/TRvlvr/model_repo/releases/download/all_public_uvr_models/{FileName}";
    public int InputLength => 4 * DimF * DimT; // [1, L re/im + R re/im, DimF, DimT]
}

// Port of the kuielab/UVR MDX-Net inference: chunks of hop*(dimT-1) samples, centre-padded STFT with a
// periodic Hann window, model, iSTFT, keep the middle of each chunk. Checked against torch.stft/istft.
public sealed class MdxSeparator
{
    readonly MdxModel model;
    readonly Fft fft;
    readonly double[] window;
    readonly double[] re, im;
    readonly int trim, chunk, gen, bins;

    public MdxSeparator(MdxModel model)
    {
        this.model = model;
        fft = new Fft(model.NFft);
        window = new double[model.NFft];
        for (var i = 0; i < model.NFft; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / model.NFft);
        re = new double[model.NFft];
        im = new double[model.NFft];
        trim = model.NFft / 2;
        chunk = model.Hop * (model.DimT - 1);
        gen = chunk - 2 * trim;
        bins = model.NFft / 2 + 1;
        if (gen <= 0 || model.DimF > bins) throw new ArgumentException("Model numbers do not fit together.", nameof(model));
    }

    // Takes interleaved stereo float PCM; returns the model's stem (interleaved, same length) times Compensate.
    // runModel maps one [1,4,DimF,DimT] tensor (flat, row-major) to an output of the same shape.
    public float[] Separate(float[] interleaved, Func<float[], float[]> runModel,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (interleaved.Length % 2 != 0) throw new ArgumentException("Stereo PCM needs an even sample count.", nameof(interleaved));
        var n = interleaved.Length / 2;
        var pad = gen - n % gen;
        // Padded mix per channel: trim zeros, the song, pad zeros, trim zeros (UVR's layout).
        var total = trim + n + pad + trim;
        var l = new float[total];
        var r = new float[total];
        for (var i = 0; i < n; i++)
        {
            l[trim + i] = interleaved[2 * i];
            r[trim + i] = interleaved[2 * i + 1];
        }

        var output = new float[interleaved.Length];
        var chunks = (n + pad) / gen;
        var input = new float[model.InputLength];
        var outL = new double[chunk];
        var outR = new double[chunk];
        for (var c = 0; c < chunks; c++)
        {
            ct.ThrowIfCancellationRequested();
            var start = c * gen;
            Analyse(l, r, start, input);
            var spec = runModel(input);
            if (spec.Length != model.InputLength) throw new InvalidOperationException("Model output has the wrong size.");
            Synthesise(spec, outL, outR);
            // Keep [trim, chunk - trim) of this chunk: song samples start + j for j in [0, gen).
            for (var j = 0; j < gen; j++)
            {
                var s = start + j;
                if (s >= n) break;
                output[2 * s] = (float)(outL[trim + j] * model.Compensate);
                output[2 * s + 1] = (float)(outR[trim + j] * model.Compensate);
            }
            progress?.Report((c + 1) / (double)chunks);
        }
        return output;
    }

    // torch.stft(center=True, pad_mode="reflect") of l/r[start, start+chunk), both channels in one complex FFT.
    void Analyse(float[] l, float[] r, int start, float[] input)
    {
        int nfft = model.NFft, dimF = model.DimF, dimT = model.DimT;
        for (var t = 0; t < dimT; t++)
        {
            for (var i = 0; i < nfft; i++)
            {
                var j = Reflect(t * model.Hop + i - trim, chunk);
                re[i] = l[start + j] * window[i];
                im[i] = r[start + j] * window[i];
            }
            fft.Forward(re, im);
            for (var f = 0; f < dimF; f++)
            {
                // Unpack two real spectra from Z = FFT(l + i r): L = (Z[f] + conj Z[-f]) / 2, R = (Z[f] - conj Z[-f]) / 2i.
                var g = (nfft - f) % nfft;
                double zr = re[f], zi = im[f], cr = re[g], ci = -im[g];
                input[Index(0, f, t)] = (float)((zr + cr) / 2);
                input[Index(1, f, t)] = (float)((zi + ci) / 2);
                input[Index(2, f, t)] = (float)((zi - ci) / 2);
                input[Index(3, f, t)] = (float)(-(zr - cr) / 2);
            }
        }
    }

    // torch.istft(center=True) of the model output, bins >= DimF zero, into outL/outR (length chunk).
    void Synthesise(float[] spec, double[] outL, double[] outR)
    {
        int nfft = model.NFft, dimF = model.DimF, dimT = model.DimT, hop = model.Hop;
        var len = nfft + hop * (dimT - 1);
        var accL = new double[len];
        var accR = new double[len];
        var env = new double[len];
        for (var t = 0; t < dimT; t++)
        {
            Array.Clear(re);
            Array.Clear(im);
            // S = L + iR over the full Hermitian spectrum; the real part of IFFT(S) is l, the imaginary part r.
            for (var f = 0; f < dimF; f++)
            {
                double lr = spec[Index(0, f, t)], li = f == 0 ? 0 : spec[Index(1, f, t)];
                double rr = spec[Index(2, f, t)], ri = f == 0 ? 0 : spec[Index(3, f, t)];
                re[f] = lr - ri; im[f] = li + rr;
                if (f == 0) continue;
                // Bin -f holds conj(L) + i conj(R).
                var g = nfft - f;
                re[g] = lr + ri; im[g] = -li + rr;
            }
            fft.Inverse(re, im);
            var o = t * hop;
            for (var i = 0; i < nfft; i++)
            {
                accL[o + i] += re[i] * window[i];
                accR[o + i] += im[i] * window[i];
                env[o + i] += window[i] * window[i];
            }
        }
        for (var i = 0; i < chunk; i++)
        {
            var e = env[trim + i];
            outL[i] = e > 1e-11 ? accL[trim + i] / e : 0;
            outR[i] = e > 1e-11 ? accR[trim + i] / e : 0;
        }
    }

    int Index(int plane, int f, int t) => (plane * model.DimF + f) * model.DimT + t;

    // Reflect padding without repeating the edge sample, like torch's "reflect".
    static int Reflect(int i, int length) => i < 0 ? -i : i >= length ? 2 * (length - 1) - i : i;
}
