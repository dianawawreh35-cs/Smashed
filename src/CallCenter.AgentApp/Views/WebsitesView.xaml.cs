using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using CallCenter.AgentApp.ViewModels;

namespace CallCenter.AgentApp.Views;

/// <summary>
/// The Websites screen (A-88): a tab bar, and up to four places, each a bar
/// and a browser.
/// </summary>
/// <remarks>
/// <b>Nothing is drawn over a browser.</b> A WebView2 is a window of its own,
/// and WPF cannot paint on top of it, so the bars sit in rows of their own
/// above each page rather than over it, and the call banner is above the whole
/// screen (A-19). A list that drops down is a pop-up window, so it is fine.
/// </remarks>
public partial class WebsitesView : UserControl, IDisposable
{
    private readonly WebsitesViewModel _viewModel;
    private readonly ContentControl[] _bars = new ContentControl[WebsitesViewModel.PlaceCount];

    public WebsitesView(WebsitesViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        var template = (DataTemplate)Resources["PlaceBar"];

        for (var i = 0; i < _bars.Length; i++)
        {
            _bars[i] = new ContentControl
            {
                Content = viewModel.Places[i],
                ContentTemplate = template,
                Margin = new Thickness(i % 2 == 1 ? 2 : 0, i >= 2 ? 2 : 0, 0, 0),
            };
            Board.Children.Add(_bars[i]);
        }

        foreach (var tab in viewModel.Tabs)
        {
            Board.Children.Add(tab.Browser);
        }

        viewModel.Tabs.CollectionChanged += OnTabsChanged;
        viewModel.ArrangementChanged += OnArrangementChanged;
        viewModel.GroupsRequested += OnGroupsRequested;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(WebsitesViewModel.ShownPlaces))
            {
                MarkLayout();
            }
        };

        Arrange();
        MarkLayout();
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var tab in e.NewItems?.OfType<WebsiteTab>() ?? [])
        {
            Board.Children.Add(tab.Browser);
        }

        foreach (var tab in e.OldItems?.OfType<WebsiteTab>() ?? [])
        {
            Board.Children.Remove(tab.Browser);
        }
    }

    private void OnArrangementChanged(object? sender, EventArgs e) => Arrange();

    private WebsiteGroupsWindow? _groups;

    /// <summary>One groups window at a time; asking again brings it forward.</summary>
    private void OnGroupsRequested(object? sender, EventArgs e)
    {
        if (_groups is { IsLoaded: true })
        {
            _groups.Activate();
            return;
        }

        _groups = new WebsiteGroupsWindow(new WebsiteGroupsEditor(_viewModel)) { Owner = Window.GetWindow(this) };
        _groups.Closed += (_, _) => _groups = null;
        _groups.Show();
    }

    /// <summary>Puts each bar and each browser in its row and column for the layout.</summary>
    private void Arrange()
    {
        var places = _viewModel.ShownPlaces;
        BottomRow.Height = places == 4 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        for (var i = 0; i < _bars.Length; i++)
        {
            Place(_bars[i], i, places, bar: true);
            _bars[i].Visibility = i < places ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var tab in _viewModel.Tabs)
        {
            if (_viewModel.PlaceOf(tab) is var index and >= 0)
            {
                Place(tab.Browser, index, places, bar: false);
                tab.Browser.Margin = _bars[index].Margin with { Top = 0 };
            }
        }
    }

    private static void Place(UIElement element, int index, int places, bool bar)
    {
        var top = places == 4 && index >= 2 ? 2 : 0;
        Grid.SetRow(element, bar ? top : top + 1);
        Grid.SetColumn(element, places == 1 ? 0 : index % 2);
        Grid.SetColumnSpan(element, places == 1 ? 2 : 1);
    }

    /// <summary>The layout button in use is the filled one.</summary>
    private void MarkLayout()
    {
        var primary = (Style)FindResource("PrimaryButton");
        var plain = (Style)Resources["LayoutButton"];

        One.Style = _viewModel.ShownPlaces == 1 ? Filled(primary) : plain;
        Two.Style = _viewModel.ShownPlaces == 2 ? Filled(primary) : plain;
        Four.Style = _viewModel.ShownPlaces == 4 ? Filled(primary) : plain;
    }

    private static Style Filled(Style primary)
    {
        var style = new Style(typeof(Button), primary);
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(10, 4, 10, 4)));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(4, 0, 0, 0)));
        return style;
    }

    public void Dispose()
    {
        _viewModel.Tabs.CollectionChanged -= OnTabsChanged;
        _viewModel.ArrangementChanged -= OnArrangementChanged;
        _viewModel.GroupsRequested -= OnGroupsRequested;
        _groups?.Close();
    }
}
