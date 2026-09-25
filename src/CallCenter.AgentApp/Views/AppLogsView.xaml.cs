using System.Windows.Controls;
using CallCenter.AgentApp.ViewModels;

namespace CallCenter.AgentApp.Views;

/// <summary>
/// App logs: the agent's own application conversations (A-71), a tab of its
/// own beside Applications, as the Call log is for calls (Dia, 25 Sep). Shares
/// Applications' view model, so what is recorded there is listed here at once.
/// </summary>
public partial class AppLogsView : UserControl
{
    /// <summary>
    /// The least the list keeps when a message is opened: the header and a few
    /// rows, enough to see the one that was opened and its neighbours.
    /// </summary>
    public const double MinListHeight = 180;

    /// <summary>The least the opened message gets, however small the window.</summary>
    private const double MinOpenMessageHeight = 120;

    /// <summary>
    /// The width the list needs to show every column whole (25 Sep): the six
    /// fixed columns (824, see the grid), the grid's margin (12), a vertical
    /// scrollbar (17) and the card's border, rounded up.
    /// </summary>
    public const double MinListWidth = 860;

    private readonly ApplicationsViewModel _viewModel;

    public AppLogsView(ApplicationsViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        SetColumnHeaders();
        viewModel.Localizer.LanguageChanged += (_, _) => SetColumnHeaders();

        // Fetched when the tab is first shown, and whenever it is shown again,
        // so the list is current however long the agent spent in Applications.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && _viewModel.RefreshCommand.CanExecute(null))
            {
                _viewModel.RefreshCommand.Execute(null);
            }
        };

        Root.SizeChanged += (_, _) => FitOpenMessage();

        // A width from the first layout pass, before the scroller has a size of
        // its own to report; FitWidth widens it to the window as soon as it does.
        Root.Width = MinListWidth;
        Frame.SizeChanged += (_, _) => FitWidth();
    }

    /// <summary>
    /// Gives the content the window's width, or the list's minimum when the
    /// window is narrower, in which case the tab scrolls sideways. A width is
    /// always set, never left to the scroller: a ScrollViewer offers its content
    /// unlimited width, and a star column cannot be measured against that
    /// (22 Sep, the call log's grid).
    /// </summary>
    private void FitWidth()
    {
        var margins = Root.Margin.Left + Root.Margin.Right;
        var available = Frame.ViewportWidth > 0 ? Frame.ViewportWidth : Frame.ActualWidth;

        Root.Width = Math.Max(MinListWidth, available - margins);
    }

    /// <summary>
    /// Caps the opened message's area at what is left once the list has its
    /// minimum, so the card and the form scroll instead of squeezing the list
    /// (the call log's fix, for the same layout).
    /// </summary>
    private void FitOpenMessage()
    {
        var header = Root.RowDefinitions[0].ActualHeight;

        OpenMessage.MaxHeight = Math.Max(MinOpenMessageHeight, Root.ActualHeight - header - MinListHeight - 80);
    }

    /// <summary>
    /// A DataGrid column header is not part of the visual tree, so it cannot
    /// bind to the view model. Set here, and again when the language changes (A-80).
    /// </summary>
    private void SetColumnHeaders()
    {
        WhenColumn.Header = _viewModel.Localizer["applications.when"];
        ChannelColumn.Header = _viewModel.Localizer["applications.channel"];
        WhoColumn.Header = _viewModel.Localizer["applications.who"];
        NumberColumn.Header = _viewModel.Localizer["applications.numberHeader"];
    }
}
