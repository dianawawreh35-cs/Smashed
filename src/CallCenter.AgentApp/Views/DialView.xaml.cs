using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CallCenter.AgentApp.ViewModels;

namespace CallCenter.AgentApp.Views;

/// <summary>The dial box: phoning a number by typing it, or on the pad (A-20).</summary>
/// <remarks>
/// The keyboard is the pad. A digit, <c>*</c> or <c>#</c> typed anywhere on this
/// screen is treated exactly as a click on that key: the key lights up, the
/// tone sounds, the digit is added. Intercepted before the number box sees it,
/// so a typed digit is not added twice. Enter dials. Backspace deletes the
/// last digit wherever the focus is; inside the box it is the box's own.
/// </remarks>
public partial class DialView : UserControl
{
    private readonly DialViewModel _viewModel;
    private readonly Dictionary<char, Button> _keys = [];

    public DialView(DialViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        foreach (var key in Pad.Children.OfType<Button>())
        {
            if (key.Content is string { Length: 1 } label)
            {
                _keys[label[0]] = key;
            }
        }

        // The number box has the focus when the screen opens, so the first
        // key pressed goes to the number rather than nowhere.
        Loaded += (_, _) => NumberBox.Focus();

        // The cursor stays at the end. A key on the pad or the keyboard adds
        // its digit through the view model, and a box given a new value puts
        // the cursor at the start, where Backspace deletes nothing. Every
        // digit is added at the end anyway, so the end is where the cursor
        // belongs.
        NumberBox.TextChanged += (_, _) => NumberBox.CaretIndex = NumberBox.Text.Length;
        NumberBox.GotKeyboardFocus += (_, _) => NumberBox.CaretIndex = NumberBox.Text.Length;
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                NumberBox.Focus();
            }
        };

        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (_viewModel.DialCommand.CanExecute(null))
            {
                _viewModel.DialCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        // Backspace anywhere on the screen deletes the last digit. Inside the
        // box it is the box's own, so the caret and a selection still work.
        if (e.Key == Key.Back && !NumberBox.IsKeyboardFocusWithin)
        {
            _viewModel.BackspaceCommand.Execute(null);
            e.Handled = true;
            return;
        }

        var key = KeyFor(e.Key, Keyboard.Modifiers);

        if (key is null || !_keys.TryGetValue(key.Value, out var button))
        {
            return;
        }

        e.Handled = true;
        _viewModel.PressKeyCommand.Execute(key.Value.ToString());
        Flash(button);
    }

    /// <summary>The pad key a keyboard key stands for, or null for anything else.</summary>
    private static char? KeyFor(Key key, ModifierKeys modifiers)
    {
        var shift = modifiers.HasFlag(ModifierKeys.Shift);

        return key switch
        {
            >= Key.D0 and <= Key.D9 when !shift => (char)('0' + (key - Key.D0)),
            >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (key - Key.NumPad0)),
            Key.Multiply => '*',
            Key.D8 when shift => '*',
            Key.D3 when shift => '#',
            _ => null,
        };
    }

    /// <summary>
    /// Lights the key the way a click does, for as long as a click's press.
    /// </summary>
    private static void Flash(Button button)
    {
        button.Background = (Brush)button.FindResource("Accent");

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            // Cleared, not set back: the key returns to whatever its style
            // says, rather than carrying a copy of it as a local value.
            button.ClearValue(BackgroundProperty);
        };
        timer.Start();
    }
}
