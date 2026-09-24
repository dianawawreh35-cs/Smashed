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

        // Never taller than the screen it opens on, less the taskbar: on a
        // small laptop the 760 in the XAML would run off the bottom, and the
        // middle row could not scroll what it could not see.
        MaxHeight = Math.Min(MaxHeight, SystemParameters.WorkArea.Height - 16);

        DataContext = viewModel;

        viewModel.CallArrived += (_, _) => BringToFront();
        viewModel.CallEnded += (_, _) => Hide();

        SizeChanged += KeepCentreWhenResized;
    }

    /// <summary>
    /// Grows and shrinks about its middle, not downwards from its top edge.
    /// </summary>
    /// <remarks>
    /// The window sizes itself to its content, and WPF keeps the top edge where
    /// it is, so opening the classification or the new-customer form pushed the
    /// bottom down and left the pop-up hanging below the middle of the screen
    /// (reported 24 Sep). Now half of any change goes above and half below, so a
    /// centred pop-up stays centred. It is kept inside the screen. And it keeps
    /// the middle it has, not the screen's: an agent who dragged it aside does
    /// not have it snatched back every time a field appears. The next call
    /// centres it again.
    /// </remarks>
    private void KeepCentreWhenResized(object sender, SizeChangedEventArgs e)
    {
        if (!e.HeightChanged || e.PreviousSize.Height <= 0 || !IsVisible)
        {
            return;
        }

        var area = SystemParameters.WorkArea;
        var top = Top - ((e.NewSize.Height - e.PreviousSize.Height) / 2);
        Top = Math.Max(area.Top, Math.Min(top, area.Bottom - e.NewSize.Height));
    }

    /// <summary>The middle of the screen's working area, clear of the taskbar.</summary>
    private void CentreOnScreen()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + ((area.Width - ActualWidth) / 2);
        Top = Math.Max(area.Top, area.Top + ((area.Height - ActualHeight) / 2));
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

        // A new caller starts at the top, not where the last one's form was
        // scrolled to, and in the middle of the screen, wherever the last one
        // was left.
        Body.ScrollToTop();
        CentreOnScreen();

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

    /// <summary>
    /// A-63: whether the name typed for a new customer is already somebody's,
    /// asked as the agent leaves the box, the same moment the Contacts tab asks.
    /// </summary>
    private void OnNewCustomerNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is CallViewModel { Caller: var caller } && caller.CheckNameCommand.CanExecute(null))
        {
            caller.CheckNameCommand.Execute(null);
        }
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
