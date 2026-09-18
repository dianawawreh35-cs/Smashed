using System.ComponentModel;
using System.Windows;
using CallCenter.AgentApp.ViewModels;

namespace CallCenter.AgentApp.Views;

/// <summary>
/// The incoming-call pop-up (A-10).
/// </summary>
/// <remarks>
/// Created once at startup and shown and hidden, never built per call: building
/// a window inside a SIP callback would put WPF construction on the critical
/// second before the caller gives up.
///
/// It is not closed by the user. Closing is intercepted and turned into hiding,
/// because a window the agent has closed would be gone for every later call,
/// and the app would look like it had stopped taking them.
/// </remarks>
public partial class CallPopupWindow : Window
{
    private bool _shuttingDown;

    public CallPopupWindow(CallViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;

        viewModel.CallArrived += (_, _) => BringToFront();
        viewModel.CallEnded += (_, _) => Hide();
    }

    /// <summary>Allows the app to close this window when it is really exiting.</summary>
    public void ShutDown()
    {
        _shuttingDown = true;
        Close();
    }

    /// <summary>
    /// A-10: the pop-up comes to the front even if the app was minimised. An
    /// agent whose app is behind a browser must not miss a call because the
    /// only sign of it was a taskbar flash.
    /// </summary>
    private void BringToFront()
    {
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        // Topmost on and straight back off: Windows refuses Activate() from a
        // process that does not have the foreground, which is exactly the case
        // this has to work in. Leaving Topmost on would pin the pop-up over
        // everything for the rest of the call, so it is turned off again once
        // the window is up.
        Topmost = true;
        Activate();
        Topmost = false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_shuttingDown)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
