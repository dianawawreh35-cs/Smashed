using System.Windows;
using CallCenter.AgentApp.ViewModels;

namespace CallCenter.AgentApp.Views;

/// <summary>The agent's groups of website tabs (A-88). Opened from the Websites screen's Groups button.</summary>
public partial class WebsiteGroupsWindow : Window
{
    public WebsiteGroupsWindow(WebsiteGroupsEditor editor)
    {
        InitializeComponent();
        DataContext = editor;
        editor.Done += (_, _) => Close();
    }
}
