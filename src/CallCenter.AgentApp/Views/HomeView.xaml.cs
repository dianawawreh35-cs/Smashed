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

        AddSection("nav.contacts", new ContactsView(services.GetRequiredService<ContactsViewModel>()));

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
