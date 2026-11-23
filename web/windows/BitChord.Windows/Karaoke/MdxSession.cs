using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BitChord.Windows.Karaoke;

// Runs one MDX-Net .onnx file: DirectML (any DX12 GPU) first, CPU when DirectML cannot start.
public sealed class MdxSession : IDisposable
{
    readonly InferenceSession session;
    readonly MdxModel model;
    readonly string inputName;

    public bool OnGpu { get; }

    public MdxSession(string modelPath, MdxModel model)
    {
        this.model = model;
        try
        {
            // DirectML needs memory patterns off and sequential execution (ORT DirectML EP docs).
            var gpu = new SessionOptions { EnableMemoryPattern = false, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
            gpu.AppendExecutionProvider_DML(0);
            session = new InferenceSession(modelPath, gpu);
            OnGpu = true;
        }
        catch (Exception e) when (e is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException)
        {
            Debug.WriteLine($"DirectML unavailable, using CPU: {e.GetType().Name}");
            session = new InferenceSession(modelPath);
        }
        inputName = session.InputMetadata.Keys.First();
    }

    public float[] Run(float[] input)
    {
        var tensor = new DenseTensor<float>(input, [1, 4, model.DimF, model.DimT]);
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)]);
        return results[0].AsTensor<float>().ToArray();
    }

    public void Dispose() => session.Dispose();
}
