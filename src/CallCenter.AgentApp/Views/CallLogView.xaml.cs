using System.Windows.Controls;
using CallCenter.AgentApp.ViewModels;

namespace CallCenter.AgentApp.Views;

/// <summary>The agent's own calls (A-50, A-52).</summary>
public partial class CallLogView : UserControl
{
    /// <summary>
    /// The least the list keeps when a call is opened: the header and about four
    /// rows, enough to see the call that was opened and its neighbours.
    /// </summary>
    public const double MinListHeight = 200;

    /// <summary>The least the opened call gets, however small the window.</summary>
    private const double MinOpenCallHeight = 120;

    private readonly CallLogViewModel _viewModel;

    public CallLogView(CallLogViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        SetColumnHeaders();
        viewModel.Localizer.LanguageChanged += (_, _) => SetColumnHeaders();

        // Loaded rather than in the constructor: the view is built when the
        // shell is, and fetching there would put an HTTP call on the path
        // between signing in and seeing a window.
        Loaded += (_, _) =>
        {
            if (_viewModel.RefreshCommand.CanExecute(null))
            {
                _viewModel.RefreshCommand.Execute(null);
            }
        };

        // A-51: a recording is not left playing to a screen nobody is looking
        // at. IsVisibleChanged rather than Unloaded, because it covers the shell
        // hiding this view as well as swapping it out.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
            {
                _viewModel.Hide();
            }
        };

        Root.SizeChanged += (_, _) => FitOpenCall();
    }

    /// <summary>
    /// Caps the opened call's area at what is left once the list has its
    /// minimum, so the card and the form scroll instead of squeezing the list.
    /// </summary>
    /// <remarks>
    /// Done here rather than in XAML because a grid measures an Auto row as if
    /// it had unlimited height, so a ScrollViewer in one never scrolls. It
    /// only scrolls once it has a MaxHeight, and that depends on the window.
    /// Without it, the details card and the form were taller than the window
    /// and the list shrank to a thin line.
    /// </remarks>
    private void FitOpenCall()
    {
        var header = Root.RowDefinitions[0].ActualHeight + Root.RowDefinitions[1].ActualHeight;

        OpenCall.MaxHeight = Math.Max(MinOpenCallHeight, Root.ActualHeight - header - MinListHeight);
    }

    /// <summary>
    /// A DataGrid column header is not part of the visual tree, so it cannot
    /// bind to the view model. Set here, and again whenever the language
    /// changes (A-80).
    /// </summary>
    private void SetColumnHeaders()
    {
        WhenColumn.Header = _viewModel.Localizer["callLog.when"];
        WhoColumn.Header = _viewModel.Localizer["callLog.who"];
        NumberColumn.Header = _viewModel.Localizer["callLog.number"];
        QueueColumn.Header = _viewModel.Localizer["callLog.queue"];
        DurationColumn.Header = _viewModel.Localizer["callLog.duration"];
        StatusColumn.Header = _viewModel.Localizer["callLog.statusHeader"];
    }
}
