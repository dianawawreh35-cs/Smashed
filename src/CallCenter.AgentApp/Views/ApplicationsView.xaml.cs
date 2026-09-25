using System.Windows;
using System.Windows.Controls;
using CallCenter.AgentApp.ViewModels;

namespace CallCenter.AgentApp.Views;

/// <summary>Applications: recording a message and the agent's own (A-70, A-71).</summary>
public partial class ApplicationsView : UserControl
{
    /// <summary>
    /// The least the list keeps when a message is opened: the header and a few
    /// rows, enough to see the one that was opened and its neighbours.
    /// </summary>
    public const double MinListHeight = 180;

    /// <summary>The least the opened message gets, however small the window.</summary>
    private const double MinOpenMessageHeight = 120;

    private readonly ApplicationsViewModel _viewModel;

    public ApplicationsView(ApplicationsViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        SetColumnHeaders();
        viewModel.Localizer.LanguageChanged += (_, _) => SetColumnHeaders();

        // Loaded rather than in the constructor, for the reason the call log
        // gives: the view is built with the shell, and an HTTP call there sits
        // between signing in and seeing a window.
        Loaded += (_, _) =>
        {
            if (_viewModel.RefreshCommand.CanExecute(null))
            {
                _viewModel.RefreshCommand.Execute(null);
            }
        };

        // A-73: the number box has the focus when the screen opens, so an
        // agent with a message to record types the number and nothing else.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                NumberBox.Focus();
            }
        };

        Root.SizeChanged += (_, _) => FitOpenMessage();
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

    /// <summary>A-63: leaving the name box asks whether it is already somebody's.</summary>
    private void OnNewCustomerNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Customer.CheckNameCommand.CanExecute(null))
        {
            _viewModel.Customer.CheckNameCommand.Execute(null);
        }
    }
}
