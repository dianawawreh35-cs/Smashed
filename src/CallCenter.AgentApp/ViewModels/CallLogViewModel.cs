using System.Collections.ObjectModel;
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
/// Filtering is done here rather than on the server. The endpoint returns the
/// agent's recent calls, which for one person on one shift is a small list, and
/// a filter that answers as the agent types beats one that makes a round trip
/// per keystroke. When the list outgrows that, the filter moves to the server;
/// the screen will not change.
/// </remarks>
public partial class CallLogViewModel : ObservableObject
{
    private readonly ApiClient _api;

    /// <summary>Everything fetched, before the filter.</summary>
    private readonly List<CommunicationDto> _all = [];

    public CallLogViewModel(ApiClient api, Localizer localizer)
    {
        _api = api;
        Localizer = localizer;

        Calls.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasCalls));

        localizer.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StatusMessage));
            Apply();
        };
    }

    public Localizer Localizer { get; }

    public ObservableCollection<CallRow> Calls { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusKey;

    /// <summary>Filters by number or contact name as it is typed.</summary>
    [ObservableProperty]
    private string _query = string.Empty;

    /// <summary>Shows only the calls that have not been classified yet (A-41).</summary>
    [ObservableProperty]
    private bool _unclassifiedOnly;

    public bool HasStatus => StatusKey is not null;

    public bool HasCalls => Calls.Count > 0;

    public string? StatusMessage => StatusKey is null ? null : Localizer[StatusKey];

    partial void OnQueryChanged(string value) => Apply();

    partial void OnUnclassifiedOnlyChanged(bool value) => Apply();

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusKey = null;

        try
        {
            var result = await _api.GetMyCallsAsync(ct: ct);

            _all.Clear();

            if (!result.IsOk || result.Value is null)
            {
                Calls.Clear();

                // A-04: the app keeps working with the server down, so this says
                // so rather than showing an empty log, which would read as "you
                // have taken no calls today".
                StatusKey = "callLog.offline";
                return;
            }

            _all.AddRange(result.Value);
            Apply();

            if (Calls.Count == 0)
            {
                StatusKey = "callLog.empty";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Re-runs the filter over what was fetched.</summary>
    private void Apply()
    {
        var query = Query.Trim();

        var matching = _all.Where(c =>
            (!UnclassifiedOnly || c.IsUnclassified)
            && (query.Length == 0
                || (c.RemoteNumberRaw?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                || (c.ContactName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)));

        Calls.Clear();

        foreach (var call in matching)
        {
            Calls.Add(new CallRow(call, Localizer));
        }

        StatusKey = Calls.Count == 0 && _all.Count > 0 ? "callLog.noMatches" : null;
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
