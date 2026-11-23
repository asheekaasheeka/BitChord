namespace BitChord.Windows;

public static class AppPaths
{
    static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BitChord");

    public static string UserDataFolder { get; } = Path.Combine(Root, "WebView2");
    public static string ExtensionFolder { get; } = Path.Combine(AppContext.BaseDirectory, "extension");
    public static string WebFolder { get; } = Path.Combine(AppContext.BaseDirectory, "web"); // web/app's production build
    public static string WindowStateFile { get; } = Path.Combine(Root, "window.json");
    public static string TrayHintFile { get; } = Path.Combine(Root, "tray-hint-shown");
    public static string LocalLibraryFile { get; } = Path.Combine(Root, "local-library.json"); // folders + scanned tags
    public static string LocalArtFolder { get; } = Path.Combine(Root, "local-art"); // extracted album art (JPEG)
    public static string ModelsFolder { get; } = Path.Combine(Root, "models"); // karaoke models, downloaded on first use
}
