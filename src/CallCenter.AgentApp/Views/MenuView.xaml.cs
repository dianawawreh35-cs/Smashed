using System.Windows.Controls;
using CallCenter.AgentApp.ViewModels;

namespace CallCenter.AgentApp.Views;

/// <summary>The menu, as the agent reads it mid-call (A-66).</summary>
public partial class MenuView : UserControl
{
    public MenuView(MenuViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // The whole menu on arrival, in printed order, so the screen is useful
        // before anything is typed - an agent who cannot spell "ماشروم" scrolls
        // to it instead.
        Loaded += (_, _) =>
        {
            if (viewModel.SearchCommand.CanExecute(null))
            {
                viewModel.SearchCommand.Execute(null);
            }
        };
    }
}
