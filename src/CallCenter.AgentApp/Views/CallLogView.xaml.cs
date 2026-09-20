using System.Windows.Controls;
using CallCenter.AgentApp.ViewModels;

namespace CallCenter.AgentApp.Views;

/// <summary>The agent's own calls (A-50, A-52).</summary>
public partial class CallLogView : UserControl
{
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
