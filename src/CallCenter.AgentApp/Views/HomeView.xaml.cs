using System.Windows;
using System.Windows.Controls;
using CallCenter.AgentApp.Services.Calls;
using CallCenter.AgentApp.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.AgentApp.Views;

/// <summary>
/// The signed-in shell: a rail of sections on the start edge, the chosen
/// section beside it.
/// </summary>
/// <remarks>
/// Built fresh at every sign-in, from a DI scope of its own, and disposed at
/// sign-out with it (M-A06). Until 27 Sep nothing was: every sign-in left the
/// last shift's screens subscribed to the language, the phone and the queue,
/// redrawn by them for the rest of the day.
/// </remarks>
public partial class HomeView : UserControl, IDisposable
{
    private readonly HomeViewModel _viewModel;
    private readonly CallLogReporter _reporter;
    private readonly List<(TabItem Tab, string LabelKey, UserControl Pane)> _sections = [];

    /// <summary>
    /// A-88: the websites, kept in the window for the whole shift rather than
    /// swapped in and out like the other sections, so the pages, their logins
    /// and their sounds carry on while the agent is elsewhere.
    /// </summary>
    private readonly WebsitesView _websites;

    public HomeView(HomeViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _reporter = services.GetRequiredService<CallLogReporter>();
        DataContext = viewModel;

        // The call log first: it is what an agent checks between calls, and
        // what they work through at the end of a shift (A-50, A-41).
        AddSection("nav.callLog", new CallLogView(services.GetRequiredService<CallLogViewModel>()));

        // A-88: the POS and the other sites, as tabs inside the app. Loaded
        // now, not when first opened: the sites that alert have to be running
        // to be heard, and the POS has to be there for the first call's cart.
        var websites = services.GetRequiredService<WebsitesViewModel>();
        _websites = new WebsitesView(websites) { Visibility = Visibility.Collapsed };
        Grid.SetColumn(_websites, 1);
        ((Grid)Pane.Parent).Children.Add(_websites);
        AddSection("nav.websites", _websites);
        _ = websites.LoadAsync();

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
        viewModel.Localizer.LanguageChanged += OnLanguageChanged;

        // F-08: the count of what the queue set aside, on the call log's button.
        _reporter.SetAsideChanged += OnSetAsideChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => SetLabels();

    private void OnSetAsideChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(SetLabels);

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
            var pane = _sections[Nav.SelectedIndex].Pane;
            var websites = ReferenceEquals(pane, _websites);

            _websites.Visibility = websites ? Visibility.Visible : Visibility.Collapsed;
            Pane.Content = websites ? null : pane;
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
            var label = _viewModel.Localizer[labelKey];

            tab.Header = labelKey == "nav.callLog" && _reporter.SetAsideCount > 0
                ? WithBadge(label, _reporter.SetAsideCount)
                : label;
        }
    }

    /// <summary>
    /// The label with a count after it, in the warning colour (F-08). On the
    /// end edge of the label, so it follows the text in Arabic.
    /// </summary>
    private static StackPanel WithBadge(string label, int count)
    {
        var badge = new Border
        {
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("Warning"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 0, 6, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = count.ToString(),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("OnWarning"),
            },
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(badge);
        return panel;
    }

    /// <summary>
    /// Takes this shell off the singletons, and each section with it (M-A06).
    /// The view models go with the scope that made them.
    /// </summary>
    public void Dispose()
    {
        _viewModel.Localizer.LanguageChanged -= OnLanguageChanged;
        _reporter.SetAsideChanged -= OnSetAsideChanged;

        foreach (var pane in _sections.Select(s => s.Pane).OfType<IDisposable>())
        {
            pane.Dispose();
        }
    }
}
