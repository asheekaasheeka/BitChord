using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace BitChord.Windows.Karaoke;

// The web player's side of karaoke: it sends a song as 44.1 kHz stereo float PCM in a shared buffer,
// this separates it in place (the buffer comes back holding the instrumental).
//
// Page → app: {karaoke:"separate", job, frames}, then {karaoke:"run", job} once the buffer is filled;
//             {karaoke:"cancel"|"release", job}.
// App → page: shared buffer with {karaoke:"buffer", job}; {karaoke:"progress", job, phase, value};
//             {karaoke:"done", job, gpu}; {karaoke:"error", job, message}.
sealed class KaraokeBridge : IDisposable
{
    static readonly MdxModel Model = MdxModel.Kara2;

    readonly Form owner;
    readonly CoreWebView2 core;
    Task<MdxSession>? session;
    Job? job;

    sealed class Job(string id, int frames)
    {
        public string Id { get; } = id;
        public int Frames { get; } = frames;
        public CoreWebView2SharedBuffer? Buffer { get; set; }
        public CancellationTokenSource Cancel { get; } = new();
    }

    public KaraokeBridge(Form owner, CoreWebView2 core)
    {
        this.owner = owner;
        this.core = core;
        core.WebMessageReceived += OnMessage;
    }

    static bool IsApp(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && u.Host.Equals(NavigationPolicy.AppHost, StringComparison.OrdinalIgnoreCase) && u.IsDefaultPort;

    void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Any page in the main window can post (Google sign-in, YouTube Music); only the player is heard.
        if (!IsApp(e.Source)) return;
        if (!KaraokeMessage.TryParse(e.WebMessageAsJson, out var m)) return;
        switch (m.Type)
        {
            case "separate": _ = StartAsync(m.Job, m.Frames); break;
            case "run": _ = RunAsync(m.Job); break;
            case "cancel":
            case "release": if (job?.Id == m.Job) End(); break;
        }
    }

    void Post(object message)
    {
        // The page may have navigated away while a job ran; nothing goes to another origin.
        if (IsApp(core.Source)) core.PostWebMessageAsJson(JsonSerializer.Serialize(message));
    }

    void Fail(Job j, string message)
    {
        if (job != j) return;
        Post(new { karaoke = "error", job = j.Id, message });
        End();
    }

    void End()
    {
        if (job == null) return;
        job.Cancel.Cancel();
        job.Buffer?.Dispose();
        job = null;
    }

    async Task StartAsync(string id, int frames)
    {
        End();
        var j = job = new Job(id, frames);
        try
        {
            var download = new Progress<double>(v => { if (job == j) Post(new { karaoke = "progress", job = id, phase = "download", value = v }); });
            var path = await ModelStore.EnsureAsync(AppPaths.ModelsFolder, Model, AskToDownload, download, j.Cancel.Token);
            if (job != j) return;
            if (path == null) { Fail(j, "declined"); return; }
            session ??= Task.Run(() => new MdxSession(path, Model));
            await session;
            if (job != j) return;
            j.Buffer = core.Environment.CreateSharedBuffer((ulong)frames * 2 * sizeof(float));
            core.PostSharedBufferToScript(j.Buffer, CoreWebView2SharedBufferAccess.ReadWrite,
                JsonSerializer.Serialize(new { karaoke = "buffer", job = id }));
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.WriteLine($"karaoke start failed: {e.GetType().Name}");
            if (session is { IsFaulted: true }) session = null;
            Fail(j, e is HttpRequestException or InvalidDataException ? "download" : "start");
        }
    }

    async Task RunAsync(string id)
    {
        var j = job;
        if (j == null || j.Id != id || j.Buffer == null || session is not { IsCompletedSuccessfully: true }) return;
        var mdx = session.Result;
        var pcm = new float[j.Frames * 2];
        Marshal.Copy((IntPtr)j.Buffer.Buffer, pcm, 0, pcm.Length);
        var progress = new Progress<double>(v => { if (job == j) Post(new { karaoke = "progress", job = id, phase = "separate", value = v }); });
        try
        {
            var token = j.Cancel.Token;
            var stem = await Task.Run(() => new MdxSeparator(Model).Separate(pcm, mdx.Run, progress, token), token);
            // Back on the UI thread: the buffer is disposed only here, so it is still alive if this is still the job.
            if (job != j || j.Buffer == null) return;
            Marshal.Copy(stem, 0, (IntPtr)j.Buffer.Buffer, stem.Length);
            Post(new { karaoke = "done", job = id, gpu = mdx.OnGpu });
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.WriteLine($"karaoke separate failed: {e.GetType().Name}");
            Fail(j, "separate");
        }
    }

    bool AskToDownload() =>
        MessageBox.Show(owner,
            "Karaoke needs a vocal-removal model (UVR MDX-Net Karaoke 2, about 50 MB).\n\n" +
            "Download it now from github.com/TRvlvr/model_repo?\n" +
            "It is saved on this PC and only used for karaoke; your songs are processed on this PC, not uploaded.",
            "BitChord karaoke", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public void Dispose()
    {
        core.WebMessageReceived -= OnMessage;
        End();
        if (session is { IsCompletedSuccessfully: true }) session.Result.Dispose();
    }
}
