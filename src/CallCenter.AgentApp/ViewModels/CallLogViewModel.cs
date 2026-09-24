using System.Collections.ObjectModel;
using System.Windows.Threading;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// The agent's own calls (A-50). Only their own — A-52, and the server decides
/// it: the endpoint takes no agent id, so there is no request this screen could
/// make that would return somebody else's.
/// </summary>
/// <remarks>
/// <b>Every filter is applied by the server.</b> The first version fetched the
/// last hundred calls and filtered those on the laptop, which is wrong in a way
/// nobody would see: a search for a customer two hundred calls ago answers "no
/// calls match", and picking a date last week returns an empty day. Both read as
/// "that never happened". A screen that says "nothing" when it means "nothing in
/// the part I looked at" is worse than one that cannot answer.
///
/// The cost is a round trip per change, which is why the text box waits for a
/// pause in typing rather than asking on every keystroke.
/// </remarks>
public partial class CallLogViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// How long the typing has to stop before the search is sent. Long enough
    /// that a number is typed in one request, short enough to feel immediate.
    /// </summary>
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(400);

    private readonly ApiClient _api;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _typingTimer;

    /// <summary>Cancels a fetch that a newer one has replaced.</summary>
    private CancellationTokenSource? _inFlight;

    public CallLogViewModel(
        ApiClient api,
        ClassificationFormViewModel classification,
        RecordingPlayerViewModel player,
        Localizer localizer,
        Dispatcher dispatcher)
    {
        _api = api;
        _dispatcher = dispatcher;
        Classification = classification;
        Player = player;
        Localizer = localizer;

        // A saved classification clears the chip on the row it belongs to, so
        // the list is refetched rather than guessed at.
        Classification.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ClassificationFormViewModel.IsSaved)
                && Classification.IsSaved)
            {
                _ = RefreshAsync(CancellationToken.None);
            }
        };

        _typingTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TypingPause,
        };

        _typingTimer.Tick += (_, _) =>
        {
            _typingTimer.Stop();
            _ = RefreshAsync(CancellationToken.None);
        };

        Calls.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasCalls));

        localizer.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(NotesHeading));
            Rebuild();

            // The open call's details are worded from the row, which reads the
            // language as it is asked, so telling the screen to ask again is
            // enough.
            OnPropertyChanged(nameof(Opened));
        };
    }

    public Localizer Localizer { get; }

    public ObservableCollection<CallRow> Calls { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusKey;

    /// <summary>Filters by number or contact name. Sent once typing pauses.</summary>
    [ObservableProperty]
    private string _query = string.Empty;

    /// <summary>Shows only the calls that have not been classified yet (A-41).</summary>
    [ObservableProperty]
    private bool _unclassifiedOnly;

    /// <summary>Earliest call to show. Null for no lower bound.</summary>
    [ObservableProperty]
    private DateTime? _from;

    /// <summary>Latest day to show, included whole. Null for no upper bound.</summary>
    [ObservableProperty]
    private DateTime? _to;

    public bool HasStatus => StatusKey is not null;

    public bool HasCalls => Calls.Count > 0;

    public string? StatusMessage => StatusKey is null ? null : Localizer[StatusKey];

    /// <summary>
    /// Typing restarts the clock, so a whole number costs one request rather
    /// than one per digit.
    /// </summary>
    partial void OnQueryChanged(string value)
    {
        _typingTimer.Stop();
        _typingTimer.Start();
    }

    // The tick boxes and dates are single decisions, so they ask at once.
    partial void OnUnclassifiedOnlyChanged(bool value) => _ = RefreshAsync(CancellationToken.None);

    partial void OnFromChanged(DateTime? value) => _ = RefreshAsync(CancellationToken.None);

    partial void OnToChanged(DateTime? value) => _ = RefreshAsync(CancellationToken.None);

    /// <summary>
    /// The form for tidying up a call that was skipped (A-41).
    /// </summary>
    /// <remarks>
    /// Its own instance, not the pop-up's. An incoming call must not wipe out
    /// what the agent is typing here.
    /// </remarks>
    public ClassificationFormViewModel Classification { get; }

    /// <summary>Plays the open call's recording (A-51). One per call log.</summary>
    public RecordingPlayerViewModel Player { get; }

    /// <summary>
    /// The call opened from the list, whose details and recording are shown
    /// (A-51). Null when none is.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    private CallRow? _opened;

    public bool IsOpen => Opened is not null;

    /// <summary>
    /// Opens a call from the list (A-51): its details and recording, and with
    /// them the classification form for an answered call (A-41, A-42) or the note
    /// for a missed, rejected or unanswered one.
    /// </summary>
    /// <remarks>
    /// <b>This sits around what double-click already did rather than replacing
    /// it.</b> The form is what an agent opens a call for nearly every time, so
    /// it still opens at once, with the details and the player above it. Putting
    /// it one click behind a details screen would slow the common case to make
    /// room for the rare one.
    ///
    /// Only an answered call is classified — nobody spoke on the others, so
    /// there is no order or complaint to record. What is worth knowing about a
    /// missed, rejected or unanswered call is why, and that is a sentence. A blocked or
    /// failed call takes neither: nobody chose anything, so it opens its details
    /// alone.
    /// </remarks>
    [RelayCommand]
    private async Task OpenAsync(CallRow? row)
    {
        if (row is null)
        {
            return;
        }

        Opened = row;

        // Not awaited: the form should not wait on a recording of several
        // megabytes, and the player says it is loading while it does.
        _ = Player.LoadAsync(row);

        if (row.CanBeClassified)
        {
            CloseNotes();
            await Classification.BeginForLoggedCallAsync(row.Id);
        }
        else if (row.TakesNotes)
        {
            Classification.Close();

            NotesFor = row;
            NotesText = row.Notes;
            NotesMessage = string.Empty;
        }
        else
        {
            Classification.Close();
            CloseNotes();
        }
    }

    /// <summary>Closes the open call: its details, its recording and its form.</summary>
    [RelayCommand]
    private void CloseCall()
    {
        Player.Close();
        Classification.Close();
        CloseNotes();
        Opened = null;
    }

    /// <summary>
    /// The call log has gone off screen. A recording playing to nobody is
    /// paused, not stopped, so coming back carries on from the same place.
    /// </summary>
    public void Hide() => Player.Pause();

    // ---- the note on a missed, rejected or unanswered call ---------------------------

    /// <summary>The call whose note is open, or null when none is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotesOpen), nameof(NotesHeading))]
    [NotifyCanExecuteChangedFor(nameof(SaveNotesCommand))]
    private CallRow? _notesFor;

    [ObservableProperty]
    private string _notesText = string.Empty;

    /// <summary>A confirmation, or why it could not be saved.</summary>
    [ObservableProperty]
    private string _notesMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveNotesCommand))]
    private bool _isSavingNotes;

    public bool IsNotesOpen => NotesFor is not null;

    /// <summary>Asks the question the note answers, for the kind of call it is.</summary>
    public string NotesHeading => Localizer[NotesFor?.StatusKey switch
    {
        CommunicationStatuses.Rejected => "callLog.notesHeadingRejected",
        CommunicationStatuses.NoAnswer => "callLog.notesHeadingNoAnswer",
        _ => "callLog.notesHeadingMissed",
    }];

    private bool CanSaveNotes => NotesFor is not null && !IsSavingNotes;

    [RelayCommand(CanExecute = nameof(CanSaveNotes))]
    private async Task SaveNotesAsync()
    {
        if (NotesFor is not { } row)
        {
            return;
        }

        IsSavingNotes = true;
        NotesMessage = string.Empty;

        try
        {
            var result = await _api.SaveCallNotesAsync(row.Id, NotesText);

            if (!result.IsOk)
            {
                // The same refusals as a classification (A-42): past the
                // window, or somebody else's call.
                NotesMessage = Localizer[result.ErrorCode switch
                {
                    "edit_window_closed" => "classification.tooOld",
                    "not_your_call" => "classification.notYours",
                    _ => "classification.saveFailed",
                }];

                return;
            }

            NotesMessage = Localizer["callLog.notesSaved"];

            // Refetched rather than patched, as a saved classification is: the
            // row shows the note, and the server's copy is the one that counts.
            _ = RefreshAsync(CancellationToken.None);
        }
        finally
        {
            IsSavingNotes = false;
        }
    }

    [RelayCommand]
    private void CloseNotes()
    {
        NotesFor = null;
        NotesText = string.Empty;
        NotesMessage = string.Empty;
    }

    /// <summary>Clears every filter and shows the most recent calls again.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        _typingTimer.Stop();

        Query = string.Empty;
        From = null;
        To = null;

        // Setting UnclassifiedOnly last: each of these triggers a refresh, and
        // the final one is the only fetch that matters.
        UnclassifiedOnly = false;

        _ = RefreshAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        _typingTimer.Stop();
        Player.Dispose();

        try
        {
            // The run that owns this disposes it too; cancelling one that has
            // already gone is not worth taking the app down for.
            _inFlight?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Everything the last fetch returned, kept only to re-render labels.</summary>
    private IReadOnlyList<CommunicationDto> _fetched = [];

    /// <summary>Whether any filter is set, so the screen can offer to clear them.</summary>
    public bool HasFilters =>
        From is not null || To is not null || UnclassifiedOnly || Query.Trim().Length > 0;

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        // A newer request replaces an older one. Without this, a slow answer for
        // "05" can land after the answer for "0599" and put the wrong rows on
        // screen - and it looks exactly like the filter not working.
        var previous = _inFlight;
        var current = new CancellationTokenSource();

        // Read once, here. Reading .Token later would throw if this source had
        // been disposed in the meantime by whoever replaced us.
        var token = current.Token;
        _inFlight = current;

        // Cancelled, and deliberately NOT disposed. The run that owns it is
        // still inside this method and about to look at its own token; a source
        // disposed underneath it throws ObjectDisposedException, which is not
        // an OperationCanceledException and so escaped the catch below. That is
        // the intermittent half of "the refresh button does not always work".
        // Each run disposes its own source in the finally.
        previous?.Cancel();

        IsBusy = true;
        StatusKey = null;
        OnPropertyChanged(nameof(HasFilters));

        try
        {
            var result = await _api.GetMyCallsAsync(
                from: From is { } from ? new DateTimeOffset(from.Date) : null,
                to: To is { } to ? new DateTimeOffset(to.Date) : null,
                query: Query,
                unclassifiedOnly: UnclassifiedOnly,
                ct: token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsOk || result.Value is null)
            {
                _fetched = [];
                Calls.Clear();

                // A-04: the app keeps working with the server down, so this says
                // so rather than showing an empty log, which would read as "you
                // have taken no calls".
                StatusKey = "callLog.offline";
                return;
            }

            _fetched = result.Value;
            Rebuild();

            StatusKey = Calls.Count > 0
                ? null
                : HasFilters ? "callLog.noMatches" : "callLog.empty";
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer request; its answer is the one that counts.
        }
        finally
        {
            if (ReferenceEquals(_inFlight, current))
            {
                IsBusy = false;
                _inFlight = null;
            }

            current.Dispose();
        }
    }

    /// <summary>
    /// Re-renders what was fetched. Used when the language changes — the rows
    /// carry translated labels, and nothing needs re-fetching to change them.
    /// </summary>
    private void Rebuild()
    {
        Calls.Clear();

        foreach (var call in _fetched)
        {
            Calls.Add(new CallRow(call, Localizer));
        }
    }
}

/// <summary>
/// One call, ready for the grid.
/// </summary>
/// <remarks>
/// A wrapper rather than binding the DTO directly, because three of the five
/// columns are not fields: the time needs the agent's locale, the duration needs
/// formatting, and the status needs translating (A-80). Doing that in converters
/// would spread one row's presentation across four files.
/// </remarks>
public class CallRow(CommunicationDto call, Localizer localizer)
{
    public Guid Id => call.Id;

    /// <summary>
    /// The time for a call from today, the date as well for an older one.
    /// </summary>
    /// <remarks>
    /// The list is not today's calls — it is the agent's last hundred, whenever
    /// they were. Showing only the time was written on the assumption that it
    /// was today's, and made a call from three days ago look like this
    /// afternoon. Today's calls are the overwhelming majority, so the date is
    /// added only where it changes the meaning.
    ///
    /// Numeric rather than a month name: it reads the same in both languages,
    /// and is narrower.
    /// </remarks>
    public string When
    {
        get
        {
            var local = call.StartedAt.ToLocalTime();

            return local.Date == DateTimeOffset.Now.Date
                ? local.ToString("HH:mm")
                : local.ToString("dd/MM HH:mm");
        }
    }

    /// <summary>The customer's name, or the number when nobody is on file.</summary>
    public string Who => string.IsNullOrWhiteSpace(call.ContactName)
        ? call.RemoteNumberRaw ?? localizer["callLog.numberWithheld"]
        : call.ContactName!;

    /// <summary>The number, always, even when a name is shown above it.</summary>
    public string Number => call.RemoteNumberRaw ?? string.Empty;

    /// <summary>Which queue it came through, blank for a direct call.</summary>
    public string Queue => call.QueueName ?? string.Empty;

    /// <summary>
    /// Talk time as <c>m:ss</c>, and blank for a call that was never answered —
    /// a missed call showing 0:00 reads as a call that was answered and silent.
    /// </summary>
    public string Duration => call.DurationSec is { } seconds
        ? $"{seconds / 60}:{seconds % 60:D2}"
        : string.Empty;

    /// <summary>Answered, Missed, Rejected or Blocked, in the agent's language.</summary>
    public string Status => localizer[$"callLog.status.{call.Status}"];

    /// <summary>The untranslated status, for decisions rather than display.</summary>
    public string StatusKey => call.Status;

    /// <summary>Only an answered call is classified (A-40).</summary>
    public bool CanBeClassified => call.CanBeClassified;

    /// <summary>A missed, rejected or unanswered call takes a note instead (A-41).</summary>
    public bool TakesNotes => call.TakesNotes;

    /// <summary>Why a missed, rejected or unanswered call went that way. Blank for the rest.</summary>
    public string Notes => call.Notes ?? string.Empty;

    public bool HasNotes => !string.IsNullOrWhiteSpace(call.Notes);

    /// <summary>
    /// True while the call has no classification (A-41). The row is marked so
    /// the agent can see at a glance what they still owe.
    /// </summary>
    public bool IsUnclassified => call.IsUnclassified;

    /// <summary>The audio is on the server and can be played (A-50, A-51).</summary>
    public bool HasRecording => call.HasRecording;

    /// <summary>
    /// Recorded, and removed since by retention (A-33). Marked differently from
    /// a call that was never recorded, so an old call does not look like a
    /// recording that failed.
    /// </summary>
    public bool RecordingExpired => call.RecordingExpired;

    /// <summary>What the mark in the list means, for its tooltip.</summary>
    public string RecordingHint => HasRecording
        ? localizer["callLog.recording.available"]
        : RecordingExpired ? localizer["callLog.recording.expiredShort"] : string.Empty;

    /// <summary>Date and time in full, for the details of an opened call.</summary>
    public string StartedAt => call.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    /// <summary>Incoming or outgoing, in the agent's language.</summary>
    public string DirectionLabel => call.Direction == Directions.Out
        ? localizer["callLog.outgoing"]
        : localizer["callLog.incoming"];
}
