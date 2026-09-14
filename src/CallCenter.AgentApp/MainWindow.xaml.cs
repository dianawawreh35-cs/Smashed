using System.Windows;

namespace CallCenter.AgentApp;

/// <summary>
/// Shell window. Empty apart from the status bar; the softphone, screen pop and
/// history views are added with their features.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Updates the text shown in the status bar.</summary>
    public void SetConnectionStatus(string status) => ConnectionStatusText.Text = status;
}
