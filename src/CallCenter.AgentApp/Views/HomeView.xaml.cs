using System.Windows.Controls;
using CallCenter.AgentApp.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.AgentApp.Views;

/// <summary>The signed-in shell: the agent's tabs.</summary>
public partial class HomeView : UserControl
{
    private readonly HomeViewModel _viewModel;
    private readonly TabItem _contactsTab;

    public HomeView(HomeViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        _contactsTab = new TabItem
        {
            Content = new ContactsView(services.GetRequiredService<ContactsViewModel>()),
        };

        Tabs.Items.Add(_contactsTab);

        SetTabHeaders();
        viewModel.Localizer.LanguageChanged += (_, _) => SetTabHeaders();
    }

    /// <summary>
    /// A TabItem header is set rather than bound, so it can be re-read when the
    /// language changes (A-80).
    /// </summary>
    private void SetTabHeaders()
    {
        _contactsTab.Header = _viewModel.Localizer["nav.contacts"];
    }
}
