using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BitChord.Windows;

// In-app window for allowed window.open() targets such as the Google sign-in popup.
sealed class PopupForm : Form
{
    readonly WebView2 webView = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = MainForm.Background };

    public PopupForm()
    {
        Text = "BitChord";
        Icon = MainForm.AppIcon;
        BackColor = MainForm.Background;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(520, 680);
        Controls.Add(webView);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkTitleBar.Apply(this);
    }

    public async Task<CoreWebView2> InitAsync(CoreWebView2Environment env)
    {
        await webView.EnsureCoreWebView2Async(env);
        var core = webView.CoreWebView2;
        Browser.Configure(core);
        core.WindowCloseRequested += (_, _) => Close();
        core.DocumentTitleChanged += (_, _) => Text = core.DocumentTitle;
        return core;
    }
}
