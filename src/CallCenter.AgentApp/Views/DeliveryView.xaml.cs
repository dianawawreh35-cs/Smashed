using System.Windows.Controls;
using CallCenter.AgentApp.ViewModels;

namespace CallCenter.AgentApp.Views;

/// <summary>Which branch delivers where, and for how much (A-65).</summary>
public partial class DeliveryView : UserControl
{
    public DeliveryView(DeliveryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // The whole list on arrival, so the screen is useful before anything is
        // typed - an agent who does not know the spelling can scroll.
        Loaded += (_, _) =>
        {
            if (viewModel.SearchCommand.CanExecute(null))
            {
                viewModel.SearchCommand.Execute(null);
            }
        };
    }
}
