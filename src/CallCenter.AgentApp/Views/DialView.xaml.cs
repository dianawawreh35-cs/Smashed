using System.Windows.Controls;
using CallCenter.AgentApp.ViewModels;

namespace CallCenter.AgentApp.Views;

/// <summary>The dial box: phoning a number by typing it (A-20).</summary>
public partial class DialView : UserControl
{
    public DialView(DialViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
