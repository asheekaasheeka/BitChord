using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BitChord.Windows;

sealed class MainForm : Form
{
    public static readonly Color Background = Color.FromArgb(18, 18, 18);
    public static readonly Icon? AppIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);

    const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    readonly WebView2 webView = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Background };

    record SavedWindow(int X, int Y, int Width, int Height, bool Maximized);

    readonly Tray tray;
    bool quitting;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    const int SwRestore = 9; // un-minimises back to normal or maximised, whichever it was

    public MainForm()
    {
        tray = new Tray(ShowFromTray, SignOut, Quit);
        Text = "BitChord";
        Icon = AppIcon;
        BackColor = Background;
        MinimumSize = new Size(480, 400);
        Size = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(webView);
        RestoreWindow();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkTitleBar.Apply(this);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            CoreWebView2Environment env;
            try
            {
                env = await CoreWebView2Environment.CreateAsync(null, AppPaths.UserDataFolder,
                    new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true });
            }
            catch (WebView2RuntimeNotFoundException)
            {
                var answer = MessageBox.Show(this,
                    "BitChord needs the Microsoft Edge WebView2 Runtime, which is not installed.\n\n" +
                    "Open the download page now?\n" + RuntimeDownloadUrl,
                    "BitChord", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                if (answer == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(RuntimeDownloadUrl) { UseShellExecute = true });
                Quit();
                return;
            }
            if (IsDisposed || Disposing) return;

            await webView.EnsureCoreWebView2Async(env);
            if (IsDisposed || Disposing) return;
            var core = webView.CoreWebView2;
            // Caught below: the message box names the missing file, then the app quits.
            var index = Path.Combine(AppPaths.WebFolder, "index.html");
            if (!File.Exists(index)) throw new FileNotFoundException($"The web player is missing. Reinstall the app.\n\nExpected file:\n{index}");
            Browser.Configure(core);
            Browser.ServeApp(core);
            await InstallExtensionAsync(core.Profile);
            if (IsDisposed || Disposing) return;
            core.Navigate(Browser.StartUri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            // async void: an escaped exception would crash the process with no message.
            if (IsDisposed || Disposing) return;
            MessageBox.Show(this, $"BitChord could not start.\n\n{ex.Message}", "BitChord", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Quit();
        }
    }

    static readonly string ExtensionMarker = Path.Combine(AppPaths.UserDataFolder, "bitchord-extension.txt");
    string? extensionVersion; // manifest version of the bundled extension, once read

    // Installs the bundled extension once, and again only when its manifest version changes.
    async Task InstallExtensionAsync(CoreWebView2Profile profile)
    {
        var manifest = Path.Combine(AppPaths.ExtensionFolder, "manifest.json");
        if (!File.Exists(manifest))
        {
            MessageBox.Show(this,
                $"The BitChord extension is missing.\n\nExpected folder:\n{AppPaths.ExtensionFolder}\n\n" +
                "BitChord will start, but playback features that need the extension will not work. Reinstall the app to fix this.",
                "BitChord", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var manifestText = File.ReadAllText(manifest);
            using var doc = JsonDocument.Parse(manifestText);
            var name = doc.RootElement.GetProperty("name").GetString() ?? "";
            // Not just the manifest version: a rebuild can change the trusted origins (or the app can move)
            // without a version bump, and the installed copy would keep pointing at stale files.
            var version = AppPaths.ExtensionFolder + "|" + Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(manifestText)));
            extensionVersion = version;
            var saved = File.Exists(ExtensionMarker) ? File.ReadAllLines(ExtensionMarker) : [];

            var installed = await profile.GetBrowserExtensionsAsync();
            var current = saved.Length == 2 ? installed.FirstOrDefault(x => x.Id == saved[0]) : null;
            if (current != null && saved[1] == version) { Browser.ExtensionId = current.Id; return; }
            if (current == null && saved.Length != 2)
            {
                // Marker lost but the extension is still there: adopt it rather than add a duplicate.
                // WebView2 drops an unpacked extension whose files changed, so a survivor matches our folder.
                var adopted = installed.FirstOrDefault(x => x.Name == name);
                if (adopted != null) { SaveExtension(adopted.Id); return; }
            }

            // Adding from the same folder reinstalls in place (the manifest key pins the id), so there is
            // no RemoveAsync here: removing would wipe chrome.storage (the Discord and Spotify settings).
            var ext = await profile.AddBrowserExtensionAsync(AppPaths.ExtensionFolder);
            SaveExtension(ext.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The BitChord extension could not be installed.\n\n{ex.Message}",
                "BitChord", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void SaveExtension(string id)
    {
        Browser.ExtensionId = id;
        try
        {
            File.WriteAllLines(ExtensionMarker, [id, extensionVersion ?? ""]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Next start finds the extension by name and adopts it again, so no reinstall loop.
            Debug.WriteLine($"extension marker not saved: {e.GetType().Name}");
        }
    }

    void RestoreWindow()
    {
        try
        {
            var s = JsonSerializer.Deserialize<SavedWindow>(File.ReadAllText(AppPaths.WindowStateFile));
            if (s == null || s.Width < MinimumSize.Width || s.Height < MinimumSize.Height) return;
            // The title bar strip must be on some screen, or the window could not be dragged back.
            var titleBar = new Rectangle(s.X, s.Y, s.Width, 40);
            if (!Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(titleBar))) return;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(s.X, s.Y, s.Width, s.Height);
            if (s.Maximized) WindowState = FormWindowState.Maximized;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Missing or corrupt state: keep the defaults.
        }
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) ShowWindow(Handle, SwRestore);
        Activate();
    }

    void ClosePopups()
    {
        foreach (var popup in Application.OpenForms.OfType<PopupForm>().ToList()) popup.Close();
    }

    void Quit()
    {
        quitting = true;
        ClosePopups();
        Close();
    }

    async void SignOut()
    {
        ShowFromTray();
        if (MessageBox.Show(this,
                "Sign out of BitChord?\n\n" +
                "This deletes this app's cookies and browsing data, and resets the BitChord extension, " +
                "which removes its Discord and Spotify settings.\n\n" +
                "It does not end your Google session on Google's servers. To revoke it, remove this PC " +
                "at myaccount.google.com > Security > Your devices.",
                "BitChord", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        ClosePopups();
        var core = webView.CoreWebView2;
        if (core == null) return;
        try
        {
            core.CookieManager.DeleteAllCookies();
            await core.Profile.ClearBrowsingDataAsync();
            if (IsDisposed || Disposing) return;
            // ClearBrowsingData is not documented to cover the extension's chrome.storage; removing the extension does.
            if (Browser.ExtensionId != null)
            {
                foreach (var x in (await core.Profile.GetBrowserExtensionsAsync()).Where(x => x.Id == Browser.ExtensionId))
                    await x.RemoveAsync();
                Browser.ExtensionId = null;
                SaveExtension((await core.Profile.AddBrowserExtensionAsync(AppPaths.ExtensionFolder)).Id);
                if (IsDisposed || Disposing) return;
            }
            core.Navigate(Browser.StartUri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Sign out did not finish.\n\n{ex.Message}", "BitChord", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        tray.Dispose();
        base.OnFormClosed(e);
    }

    bool closeCommand;

    protected override void WndProc(ref Message m)
    {
        // X, Alt+F4 and the taskbar's Close all arrive as SC_CLOSE. CloseReason alone is not enough:
        // WinForms keeps UserClosing after a cancelled close, so a later Task Manager close would hide too.
        if (m.Msg == 0x0112 && ((int)m.WParam & 0xFFF0) == 0xF060) closeCommand = true;
        try
        {
            base.WndProc(ref m);
        }
        finally
        {
            closeCommand = false;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // X hides to tray; Quit, shutdown, logoff and Task Manager really exit.
        if (closeCommand && e.CloseReason == CloseReason.UserClosing && !quitting)
        {
            e.Cancel = true;
            Hide();
            tray.HintOnce();
            return;
        }
        base.OnFormClosing(e);
        if (e.Cancel) return;
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.WindowStateFile)!);
            File.WriteAllText(AppPaths.WindowStateFile, JsonSerializer.Serialize(
                new SavedWindow(b.X, b.Y, b.Width, b.Height, WindowState == FormWindowState.Maximized)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth blocking exit over.
        }
    }
}
