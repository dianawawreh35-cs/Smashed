using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace CallCenter.AgentApp.Views;

/// <summary>
/// A window a website opened, such as a receipt to print (A-88). Signed in as
/// the tab that opened it; it closes when the page closes itself.
/// </summary>
public sealed class WebsitePopupWindow : Window
{
    public WebsitePopupWindow(string title)
    {
        Title = title;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app.ico"));
        Width = 900;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Browser = new WebView2();
        Content = Browser;

        Browser.CoreWebView2InitializationCompleted += (_, e) =>
        {
            if (e.IsSuccess)
            {
                Browser.CoreWebView2.WindowCloseRequested += (_, _) => Close();
                Browser.CoreWebView2.DocumentTitleChanged += (_, _) => Title = Browser.CoreWebView2.DocumentTitle;
            }
        };

        Closed += (_, _) => Browser.Dispose();
    }

    public WebView2 Browser { get; }
}
