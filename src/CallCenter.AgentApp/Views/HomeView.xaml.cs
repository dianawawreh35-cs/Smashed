using System.Windows.Controls;
using CallCenter.AgentApp.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.AgentApp.Views;

/// <summary>
/// The signed-in shell: a rail of sections on the start edge, the chosen
/// section beside it.
/// </summary>
public partial class HomeView : UserControl
{
    private readonly HomeViewModel _viewModel;
    private readonly List<(TabItem Tab, string LabelKey, UserControl Pane)> _sections = [];

    public HomeView(HomeViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        // The call log first: it is what an agent checks between calls, and
        // what they work through at the end of a shift (A-50, A-41).
        AddSection("nav.callLog", new CallLogView(services.GetRequiredService<CallLogViewModel>()));

        // A-70: conversations that came on an app rather than by phone. Its own
        // section, next to the call log, which stays calls only. Recording one
        // is Applications; the agent's own list is App logs, a tab of its own
        // (Dia, 25 Sep). One view model for both, so what is recorded in one is
        // listed in the other the moment it is saved.
        var applications = services.GetRequiredService<ApplicationsViewModel>();
        AddSection("nav.applications", new ApplicationsView(applications));
        AddSection("nav.appLogs", new AppLogsView(applications));

        AddSection("nav.contacts", new ContactsView(services.GetRequiredService<ContactsViewModel>()));

        // A-20: ringing a number that is not already on a screen somewhere.
        AddSection("nav.dial", new DialView(services.GetRequiredService<DialViewModel>()));

        // A-65: which branch delivers where, and for how much. Read-only.
        AddSection("nav.delivery", new DeliveryView(services.GetRequiredService<DeliveryViewModel>()));

        // A-66: what is on the menu, what is in it and what it costs.
        AddSection("nav.menu", new MenuView(services.GetRequiredService<MenuViewModel>()));

        Nav.SelectionChanged += (_, _) => ShowSelected();
        Nav.SelectedIndex = 0;
        ShowSelected();

        SetLabels();
        viewModel.Localizer.LanguageChanged += (_, _) => SetLabels();
    }

    private void AddSection(string labelKey, UserControl pane)
    {
        var tab = new TabItem();
        Nav.Items.Add(tab);
        _sections.Add((tab, labelKey, pane));
    }

    /// <summary>
    /// The rail carries only the labels; the panes live beside it. Keeping the
    /// content out of the TabControl is what lets the rail be a plain stack of
    /// buttons rather than a strip of tabs.
    /// </summary>
    private void ShowSelected()
    {
        if (Nav.SelectedIndex >= 0 && Nav.SelectedIndex < _sections.Count)
        {
            Pane.Content = _sections[Nav.SelectedIndex].Pane;
        }
    }

    /// <summary>
    /// A TabItem header is set rather than bound, so it can be re-read when the
    /// language changes (A-80).
    /// </summary>
    private void SetLabels()
    {
        foreach (var (tab, labelKey, _) in _sections)
        {
            tab.Header = _viewModel.Localizer[labelKey];
        }
    }
}
