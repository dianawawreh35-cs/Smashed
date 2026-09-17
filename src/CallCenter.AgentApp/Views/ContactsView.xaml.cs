using System.Windows;
using System.Windows.Controls;
using CallCenter.AgentApp.ViewModels;

namespace CallCenter.AgentApp.Views;

/// <summary>The shared contact list (A-61, A-63).</summary>
public partial class ContactsView : UserControl
{
    private readonly ContactsViewModel _viewModel;

    public ContactsView(ContactsViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        SetColumnHeaders();
        viewModel.Localizer.LanguageChanged += (_, _) => SetColumnHeaders();
    }

    /// <summary>
    /// A GridViewColumn header is not part of the visual tree, so it cannot bind
    /// to the view model. Set here instead, and again whenever the language
    /// changes (A-80).
    /// </summary>
    private void SetColumnHeaders()
    {
        NameColumn.Header = _viewModel.Localizer["contacts.name"];
        AddressColumn.Header = _viewModel.Localizer["contacts.address"];
    }

    /// <summary>
    /// Checks for contacts already carrying this name when the agent leaves the
    /// box (A-63) — on leaving rather than on each keystroke, so the warning
    /// appears once the name is finished rather than flickering through every
    /// prefix of it.
    /// </summary>
    private void OnNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CheckNameCommand.CanExecute(null))
        {
            _viewModel.CheckNameCommand.Execute(null);
        }
    }
}
