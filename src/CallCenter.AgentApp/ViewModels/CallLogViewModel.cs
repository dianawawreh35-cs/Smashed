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

    public CallLogViewModel(ApiClient api, Localizer localizer, Dispatcher dispatcher)
    {
        _api = api;
        _dispatcher = dispatcher;
        Localizer = localizer;

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
            Rebuild();
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
        _inFlight?.Cancel();
        _inFlight?.Dispose();
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
        _inFlight = current;
        previous?.Cancel();
        previous?.Dispose();

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
                ct: current.Token);

            if (current.Token.IsCancellationRequested)
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
            }
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

    /// <summary>
    /// True while the call has no classification (A-41). The row is marked so
    /// the agent can see at a glance what they still owe.
    /// </summary>
    public bool IsUnclassified => call.IsUnclassified;
}
