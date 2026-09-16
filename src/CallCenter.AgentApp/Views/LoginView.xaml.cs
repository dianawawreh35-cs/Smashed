using System.Windows;
using System.Windows.Controls;
using CallCenter.AgentApp.ViewModels;

namespace CallCenter.AgentApp.Views;

/// <summary>Sign-in screen (A-01).</summary>
public partial class LoginView : UserControl
{
    public LoginView(LoginViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // A pre-filled username means the same agent is back on this laptop, so
        // start where they have something to type.
        Loaded += (_, _) =>
        {
            if (viewModel.StartInPasswordBox)
            {
                PasswordBox.Focus();
            }
            else
            {
                LoginBox.Focus();
            }
        };
    }

    /// <summary>
    /// PasswordBox.Password is deliberately not bindable, so the value is pushed
    /// across by hand rather than held in the binding engine.
    /// </summary>
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel viewModel)
        {
            viewModel.Password = PasswordBox.Password;
        }
    }
}
