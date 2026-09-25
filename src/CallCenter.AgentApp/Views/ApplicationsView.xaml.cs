using System.Windows;
using System.Windows.Controls;
using CallCenter.AgentApp.ViewModels;

namespace CallCenter.AgentApp.Views;

/// <summary>
/// Applications: recording one conversation that came on an app (A-70). The
/// agent's own list is App logs (<see cref="AppLogsView"/>), its own tab, and
/// the two share one view model.
/// </summary>
public partial class ApplicationsView : UserControl
{
    private readonly ApplicationsViewModel _viewModel;

    public ApplicationsView(ApplicationsViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        // The channels and the Applications form arrive with the first refresh.
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
