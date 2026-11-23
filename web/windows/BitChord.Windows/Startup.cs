using Microsoft.Win32;

namespace BitChord.Windows;

// "Start with Windows": a per-user Run entry, so no admin rights and no installer needed.
static class Startup
{
    public const string Arg = "--startup";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "BitChord";

    static string Command => $"\"{Environment.ProcessPath ?? Application.ExecutablePath}\" {Arg}";

    // Only an entry pointing at this exe counts: a moved app leaves a stale one that would start nothing.
    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return string.Equals(key?.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(Name, Command);
            else key.DeleteValue(Name, throwOnMissingValue: false);
        }
    }

    public static bool LaunchedAtStartup => Environment.GetCommandLineArgs().Contains(Arg);
}
