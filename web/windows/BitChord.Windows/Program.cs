using System.Runtime.InteropServices;

namespace BitChord.Windows;

static class Program
{
    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);
    const int AsfwAny = -1;

    [STAThread]
    static void Main()
    {
#if !DEBUG
        // These variables let whoever launches us change the browser: a remote-debugging port (which can read
        // every cookie), another runtime, profile or channel. Release builds ignore them.
        foreach (var name in new[] { "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER",
                     "WEBVIEW2_USER_DATA_FOLDER", "WEBVIEW2_RELEASE_CHANNEL_PREFERENCE", "WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER" })
            Environment.SetEnvironmentVariable(name, null);
#endif
        using var mutex = new Mutex(true, @"Local\BitChord.Windows", out var firstInstance);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\BitChord.Windows.Show");
        if (!firstInstance)
        {
            // We own the foreground (the user just launched us), so let the first instance take it.
            AllowSetForegroundWindow(AsfwAny);
            showSignal.Set();
            return;
        }

        ApplicationConfiguration.Initialize();
        var form = new MainForm();
        var wait = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) =>
        {
            try
            {
                if (form.IsHandleCreated) form.BeginInvoke(form.ShowFromTray);
            }
            catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
            {
                // The window is going away; nothing to show.
            }
        }, null, Timeout.Infinite, executeOnlyOnce: false);

        Application.Run(form);
        wait.Unregister(null);
    }
}
