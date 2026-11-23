using System.Runtime.InteropServices;

namespace BitChord.Windows;

static class DarkTitleBar
{
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // Best effort: older Windows 10 builds ignore attribute 20, which is harmless.
    public static void Apply(Form form)
    {
        int on = 1;
        DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
    }
}
