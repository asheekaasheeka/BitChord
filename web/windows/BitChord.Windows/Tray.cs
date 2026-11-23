namespace BitChord.Windows;

// Notification-area icon: the window hides here on close so playback keeps going.
sealed class Tray : IDisposable
{
    readonly NotifyIcon icon;

    public Tray(Action show, Action signOut, Action quit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => show());
        menu.Items.Add("Sign out", null, (_, _) => signOut());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => quit());
        icon = new NotifyIcon
        {
            Icon = MainForm.AppIcon ?? SystemIcons.Application,
            Text = "BitChord",
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.DoubleClick += (_, _) => show();
    }

    // Tells the user once (ever) where the window went; the marker file records that it was shown.
    public void HintOnce()
    {
        if (File.Exists(AppPaths.TrayHintFile)) return;
        icon.ShowBalloonTip(5000, "BitChord is still running",
            "Music keeps playing. Use the tray icon to open BitChord again, or choose Quit to exit.", ToolTipIcon.Info);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.TrayHintFile)!);
            File.WriteAllText(AppPaths.TrayHintFile, "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Worst case the hint shows again next time.
        }
    }

    public void Dispose()
    {
        icon.Visible = false; // removes the icon now instead of leaving a ghost until mouse-over
        icon.ContextMenuStrip?.Dispose();
        icon.Dispose();
    }
}
