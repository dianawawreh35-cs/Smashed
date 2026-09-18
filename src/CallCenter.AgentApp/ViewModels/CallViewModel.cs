using System.Windows.Threading;
using CallCenter.AgentApp.Services.Calls;
using CallCenter.AgentApp.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// The incoming-call pop-up (A-10, A-12).
/// </summary>
/// <remarks>
/// One instance for the process, living as long as the app: a pop-up created
/// when the phone rings would have to be built inside a SIP callback, on a
/// background thread, in the second before the caller gives up. Instead the
/// window exists from startup and is shown and hidden.
///
/// Every <see cref="CallService"/> event arrives on a background thread, so
/// each one is marshalled to the dispatcher here. This is the only place that
/// crossing happens.
/// </remarks>
public partial class CallViewModel : ObservableObject
{
    private readonly CallService _calls;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;

    public CallViewModel(CallService calls, Localizer localizer, Dispatcher dispatcher)
    {
        _calls = calls;
        _dispatcher = dispatcher;
        Localizer = localizer;

        // One tick a second is enough for a timer showing whole seconds, and
        // cheap enough to leave running only while a call is connected.
        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _timer.Tick += (_, _) => OnPropertyChanged(nameof(Duration));

        localizer.LanguageChanged += (_, _) => RefreshLabels();
        _calls.StateChanged += OnStateChanged;
    }

    public Localizer Localizer { get; }

    /// <summary>Raised when the pop-up should come to the front (A-10).</summary>
    public event EventHandler? CallArrived;

    /// <summary>Raised when the call is over and the pop-up should go away.</summary>
    public event EventHandler? CallEnded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRinging))]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(Duration))]
    [NotifyPropertyChangedFor(nameof(Number))]
    [NotifyPropertyChangedFor(nameof(Queue))]
    [NotifyPropertyChangedFor(nameof(HasQueue))]
    [NotifyPropertyChangedFor(nameof(CallerName))]
    [NotifyPropertyChangedFor(nameof(HasCallerName))]
    private CallState _state = CallState.Idle;

    public bool IsRinging => State.Status is CallStatus.Ringing;

    public bool IsConnected => State.Status is CallStatus.Connected;

    /// <summary>
    /// The caller's number, or a label when it was withheld. A blank line would
    /// read as a broken screen at the moment the agent most needs to trust it.
    /// </summary>
    public string Number => string.IsNullOrWhiteSpace(State.Number)
        ? Localizer["call.numberWithheld"]
        : State.Number!;

    /// <summary>Ringing, or connected — the line above the number.</summary>
    public string StatusText => Localizer[IsConnected ? "call.connected" : "call.incoming"];

    /// <summary>
    /// The queue this call came through, above the number, because it changes
    /// how the agent answers before they have said anything.
    /// </summary>
    public string Queue => State.Queue ?? string.Empty;

    public bool HasQueue => !string.IsNullOrWhiteSpace(State.Queue);

    /// <summary>
    /// The name the PBX sent, if any. Shown small under the number: it is
    /// whatever the switch felt like sending, not a contact, and it will be
    /// replaced by the real customer lookup (A-11).
    /// </summary>
    public string CallerName => State.CallerName ?? string.Empty;

    public bool HasCallerName => !string.IsNullOrWhiteSpace(State.CallerName);

    /// <summary>
    /// How long the agent has been talking, as <c>m:ss</c>. Empty before the
    /// call is answered: a timer counting how long a phone has been ringing
    /// would read as call duration and end up in somebody's report.
    /// </summary>
    public string Duration => State.Duration is { } elapsed
        ? $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}"
        : string.Empty;

    [RelayCommand]
    private Task AnswerAsync() => _calls.AnswerAsync();

    [RelayCommand]
    private void Reject() => _calls.Reject();

    [RelayCommand]
    private void HangUp() => _calls.HangUp();

    /// <summary>
    /// Brings the call onto the screen, or takes it away. Marshalled: this
    /// arrives on a SIP thread.
    /// </summary>
    private void OnStateChanged(object? sender, CallState state) =>
        _dispatcher.Invoke(() =>
        {
            var wasActive = State.IsActive;

            State = state;

            if (state.Status is CallStatus.Connected)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }

            if (state.IsActive && !wasActive)
            {
                CallArrived?.Invoke(this, EventArgs.Empty);
            }
            else if (!state.IsActive && wasActive)
            {
                CallEnded?.Invoke(this, EventArgs.Empty);
            }
        });

    private void RefreshLabels()
    {
        OnPropertyChanged(nameof(Number));
        OnPropertyChanged(nameof(StatusText));
    }
}
