using System.Windows.Threading;
using CallCenter.AgentApp.Services.Calls;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.Shared.Contracts.Communications;
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
    private readonly CallLogReporter _reporter;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;

    public CallViewModel(
        CallService calls,
        CallLogReporter reporter,
        ClassificationFormViewModel classification,
        CallerViewModel caller,
        Localizer localizer,
        Dispatcher dispatcher)
    {
        _calls = calls;
        _reporter = reporter;
        _dispatcher = dispatcher;
        Classification = classification;
        Caller = caller;
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
        _calls.CallFinished += OnCallFinished;

        // The PBX's name hides itself once the real contact arrives, and that
        // arrives on another object, so this view model has to be told.
        Caller.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CallerViewModel.IsKnown))
            {
                OnPropertyChanged(nameof(HasCallerName));
            }
        };

        // The pop-up outlives the call while the form is still unfinished, so
        // what closes it is the form being dealt with, not the call ending.
        Classification.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ClassificationFormViewModel.IsUnfinished)
                && !Classification.IsUnfinished
                && !State.IsActive
                && !Caller.IsFormOpen)
            {
                Classification.Close();
                Caller.Clear();
                CallEnded?.Invoke(this, EventArgs.Empty);
            }
        };

        // A new-customer form being filled in keeps the pop-up too (A-11): an
        // unknown caller who rang off mid-sentence still has to be saved. Once
        // it is saved or cancelled, the pop-up goes if nothing else holds it.
        Caller.FormFinished += (_, _) =>
        {
            if (!State.IsActive && !Classification.IsUnfinished && !IsNotesOpen)
            {
                Classification.Close();
                Caller.Clear();
                CallEnded?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public Localizer Localizer { get; }

    /// <summary>
    /// The classification form, on screen from the moment the call is answered
    /// (A-40).
    /// </summary>
    public ClassificationFormViewModel Classification { get; }

    /// <summary>
    /// Who is calling — the contact behind the number (A-10, A-11, A-16). Fills
    /// in underneath the pop-up rather than holding it up.
    /// </summary>
    public CallerViewModel Caller { get; }

    /// <summary>Raised when the pop-up should come to the front (A-10).</summary>
    public event EventHandler? CallArrived;

    /// <summary>Raised when the call is over and the pop-up should go away.</summary>
    public event EventHandler? CallEnded;

    // ---- the note on an outbound call nobody picked up (A-41) -------------

    /// <summary>
    /// The call the note belongs to, while the note is on screen. Null
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// Only an outbound call the customer did not pick up. It is never
    /// classified — nobody spoke — but "rang twice, try after 6" is exactly
    /// what the next agent to call them needs to know, and the moment the call
    /// ends is when the agent knows it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotesOpen))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(Number))]
    private FinishedCall? _notesFor;

    [ObservableProperty]
    private string _notesText = string.Empty;

    public bool IsNotesOpen => NotesFor is not null;

    /// <summary>
    /// Keeps the note and lets the pop-up go. Queued behind the call, which has
    /// only just been reported, so it cannot arrive first.
    /// </summary>
    [RelayCommand]
    private async Task SaveNotesAsync()
    {
        if (NotesFor is { } call && Classification.Extension is { } extension
            && !string.IsNullOrWhiteSpace(NotesText))
        {
            await _reporter.SaveNotesAsync(
                new SaveCallNotesByCallRequest(call.SipCallId, extension, NotesText.Trim()));
        }

        FinishNotes();
    }

    /// <summary>No note. The call is logged all the same.</summary>
    [RelayCommand]
    private void SkipNotes() => FinishNotes();

    private void FinishNotes()
    {
        CloseNotes();

        if (!State.IsActive && !Caller.IsFormOpen)
        {
            Caller.Clear();
            CallEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CloseNotes()
    {
        NotesFor = null;
        NotesText = string.Empty;
    }

    /// <summary>
    /// Opens the note when an outbound call ends unanswered. Raised just before
    /// the state goes idle, so the pop-up knows to stay.
    /// </summary>
    private void OnCallFinished(object? sender, FinishedCall call)
    {
        if (!call.IsOutbound || call.Outcome is not CallOutcome.NoAnswer)
        {
            return;
        }

        _dispatcher.Invoke(() =>
        {
            NotesFor = call;
            NotesText = string.Empty;
        });
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRinging))]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsDialling))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(Duration))]
    [NotifyPropertyChangedFor(nameof(Number))]
    [NotifyPropertyChangedFor(nameof(Queue))]
    [NotifyPropertyChangedFor(nameof(HasQueue))]
    [NotifyPropertyChangedFor(nameof(CallerName))]
    [NotifyPropertyChangedFor(nameof(HasCallerName))]
    [NotifyPropertyChangedFor(nameof(IsMuted))]
    [NotifyPropertyChangedFor(nameof(MuteLabel))]
    [NotifyPropertyChangedFor(nameof(IsOnHold))]
    [NotifyPropertyChangedFor(nameof(HoldLabel))]
    private CallState _state = CallState.Idle;

    public bool IsRinging => State.Status is CallStatus.Ringing;

    public bool IsConnected => State.Status is CallStatus.Connected;

    /// <summary>
    /// A call this agent placed, not yet answered (A-20). Kept apart from
    /// <see cref="IsRinging"/> so the pop-up never offers Answer and Reject for
    /// a call the agent made.
    /// </summary>
    public bool IsDialling => State.Status is CallStatus.Dialling;

    /// <summary>
    /// The caller's number, or a label when it was withheld. A blank line would
    /// read as a broken screen at the moment the agent most needs to trust it.
    /// </summary>
    public string Number => (State.IsActive ? State.Number : NotesFor?.Number ?? State.Number) is { } number
                            && !string.IsNullOrWhiteSpace(number)
        ? number
        : Localizer["call.numberWithheld"];

    /// <summary>The microphone is paused (A-12).</summary>
    public bool IsMuted => State.IsMuted;

    /// <summary>
    /// What the mute button offers next. It reads as the action, not the state:
    /// the state is shown separately, so "Unmute" while muted is the button
    /// telling the agent what pressing it will do.
    /// </summary>
    public string MuteLabel => Localizer[IsMuted ? "call.unmute" : "call.mute"];

    /// <summary>The customer is on hold (A-12).</summary>
    public bool IsOnHold => State.IsOnHold;

    /// <summary>Hold, then Resume: the action, as with <see cref="MuteLabel"/>.</summary>
    public string HoldLabel => Localizer[IsOnHold ? "call.resume" : "call.hold"];

    /// <summary>
    /// Ringing, connected, muted or on hold — the line above the number. Hold
    /// outranks mute because it is the larger fact: nobody hears anybody. Both
    /// take the line over from "Connected" because an agent who has forgotten
    /// either is talking to nobody, and this is the one place they look.
    /// <para>
    /// The pop-up's window title binds to this too, rather than to a fixed
    /// "Incoming call" as it used to: the title bar and the taskbar button are
    /// what an agent with the pop-up behind something else can see, and they
    /// announced an arriving call while it had been connected for two minutes.
    /// </para>
    /// </summary>
    public string StatusText => Localizer[
        !State.IsActive && IsNotesOpen ? "call.notAnswered"
        : IsOnHold ? "call.onHold"
        : IsMuted ? "call.muted"
        : IsConnected ? "call.connected"
        : IsDialling ? "call.dialling"
        : "call.incoming"];

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
    /// <summary>
    /// The name the PBX sent. Hidden once the real contact is known: two names
    /// for one caller, one of them whatever the switch felt like sending, is
    /// worse than either alone.
    /// </summary>
    public string CallerName => State.CallerName ?? string.Empty;

    public bool HasCallerName =>
        !string.IsNullOrWhiteSpace(State.CallerName) && !Caller.IsKnown;

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

    [RelayCommand]
    private void ToggleMute() => _calls.ToggleMute();

    [RelayCommand]
    private void ToggleHold() => _calls.ToggleHold();

    /// <summary>
    /// Brings the call onto the screen, or takes it away. Marshalled: this
    /// arrives on a SIP thread.
    /// </summary>
    private void OnStateChanged(object? sender, CallState state) =>
        _dispatcher.Invoke(() =>
        {
            var wasActive = State.IsActive;
            var wasConnected = State.Status is CallStatus.Connected;

            State = state;

            if (state.Status is CallStatus.Connected)
            {
                _timer.Start();

                // A-40: the agent takes the order while the customer is
                // speaking, so the form is there from the answer rather than
                // appearing once they have gone.
                if (!wasConnected)
                {
                    Classification.Begin(state.SipCallId, Classification.Extension);
                }
            }
            else
            {
                _timer.Stop();
            }

            if (state.IsActive && !wasActive)
            {
                // A new call replaces whatever was still on screen. A form or
                // note left untouched from the last call is a skip (A-41), not
                // something to hold the next caller up over.
                Classification.Close();
                CloseNotes();

                // Who it is (A-10). Started here, not awaited: the pop-up is on
                // screen and the phone is ringing whatever the server does.
                Caller.Begin(state.Number);

                CallArrived?.Invoke(this, EventArgs.Empty);
            }
            else if (!state.IsActive && wasActive && !Classification.IsUnfinished && !IsNotesOpen
                     && !Caller.IsFormOpen)
            {
                Caller.Clear();
                CallEnded?.Invoke(this, EventArgs.Empty);
            }
        });

    private void RefreshLabels()
    {
        OnPropertyChanged(nameof(Number));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(MuteLabel));
        OnPropertyChanged(nameof(HoldLabel));
    }
}
