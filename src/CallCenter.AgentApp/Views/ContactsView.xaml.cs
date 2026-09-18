using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CallCenter.AgentApp.ViewModels;
using CallCenter.Shared.Contracts.Contacts;

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
    /// A DataGrid column header is not part of the visual tree, so it cannot
    /// bind to the view model. Set here, and again whenever the language
    /// changes (A-80).
    /// </summary>
    private void SetColumnHeaders()
    {
        NameColumn.Header = _viewModel.Localizer["contacts.name"];
        AddressColumn.Header = _viewModel.Localizer["contacts.address"];
    }

    /// <summary>
    /// Opens the contact that was double-clicked (A-63) — the same thing the
    /// row's Edit button does.
    /// </summary>
    /// <remarks>
    /// Wired to the row rather than to the grid, so a double-click on a column
    /// header or the scrollbar does nothing. The Edit button stays: it is what
    /// makes the action discoverable, and double-click alone is unreachable
    /// from the keyboard.
    /// </remarks>
    private void OnContactRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { Item: ContactSummaryDto contact }
            && _viewModel.EditCommand.CanExecute(contact))
        {
            _viewModel.EditCommand.Execute(contact);
        }
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
