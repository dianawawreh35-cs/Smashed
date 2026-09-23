using System.Windows.Threading;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.Shared.Contracts.Contacts;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// Who is calling: the contact behind the number, on the pop-up (A-10, A-11,
/// A-16).
/// </summary>
/// <remarks>
/// Until this existed the pop-up showed a phone number and nothing else, which
/// is most of what the pop-up is for.
///
/// <b>The lookup never holds the call up.</b> It starts when the call appears
/// and fills in underneath it. The number, the queue and the Answer button are
/// on screen from the first moment whatever the server is doing, because A-04
/// requires the phone to work with the server down and because a ringing phone
/// does not wait for HTTP.
/// </remarks>
public partial class CallerViewModel : ObservableObject
{
    /// <summary>How far the lookup has got.</summary>
    public enum Lookup
    {
        /// <summary>No call, or a caller who withheld their number.</summary>
        None,

        /// <summary>Asking the server.</summary>
        Searching,

        /// <summary>The number belongs to a contact.</summary>
        Found,

        /// <summary>The server is certain this number is on nobody's record (A-11).</summary>
        NewCustomer,

        /// <summary>
        /// The server could not be asked. <b>Not</b> the same as a new customer,
        /// and the difference matters: telling an agent a regular is new invites
        /// them to type a second record for somebody already on file.
        /// </summary>
        Unknown,
    }

    private readonly ApiClient _api;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger<CallerViewModel> _logger;

    /// <summary>
    /// Cancels the lookup for a call that is no longer on screen.
    /// </summary>
    /// <remarks>
    /// Without this a slow answer for the last caller lands on the next one's
    /// pop-up, and the agent greets somebody by a stranger's name. Rare and
    /// very hard to reproduce, which is exactly why it is handled here rather
    /// than waited for.
    /// </remarks>
    private CancellationTokenSource? _lookup;

    public CallerViewModel(ApiClient api, Localizer localizer, Dispatcher dispatcher, ILogger<CallerViewModel> logger)
    {
        _api = api;
        _dispatcher = dispatcher;
        _logger = logger;
        Localizer = localizer;

        localizer.LanguageChanged += (_, _) => OnPropertyChanged(nameof(StatusText));
    }

    public Localizer Localizer { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    [NotifyPropertyChangedFor(nameof(IsKnown))]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private Lookup _state = Lookup.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasName))]
    private string? _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddress))]
    private string? _address;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotes))]
    private string? _notes;

    /// <summary>A-16: the badge that changes how the agent speaks.</summary>
    [ObservableProperty]
    private bool _isVip;

    /// <summary>Why they are VIP (S-45), shown beside the badge.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFlagReason))]
    private string? _flagReason;

    /// <summary>How many of this customer's calls were orders (A-10).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTotals))]
    private int _orders;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTotals))]
    private int _complaints;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTotals))]
    private int _cancellations;

    /// <summary>
    /// Whether there is any history worth a line. A customer with nothing
    /// counted shows no totals rather than three zeroes, which read as a
    /// verdict on them rather than on our records.
    /// </summary>
    public bool HasTotals => Orders > 0 || Complaints > 0 || Cancellations > 0;

    public bool IsSearching => State is Lookup.Searching;

    public bool IsKnown => State is Lookup.Found;

    public bool HasName => !string.IsNullOrWhiteSpace(Name);

    public bool HasAddress => !string.IsNullOrWhiteSpace(Address);

    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public bool HasFlagReason => IsVip && !string.IsNullOrWhiteSpace(FlagReason);

    /// <summary>Whether there is a line to show in place of a contact.</summary>
    public bool HasStatus => State is Lookup.Searching or Lookup.NewCustomer or Lookup.Unknown;

    /// <summary>
    /// Looking, new customer, or could not check. Each is a different thing for
    /// the agent to do, so none of them is left blank.
    /// </summary>
    public string StatusText => State switch
    {
        Lookup.Searching => Localizer["caller.checking"],
        Lookup.NewCustomer => Localizer["caller.newCustomer"],
        Lookup.Unknown => Localizer["caller.notChecked"],
        _ => string.Empty,
    };

    /// <summary>
    /// Finds who a number belongs to. Safe to call on a SIP thread: everything
    /// that touches a property is marshalled.
    /// </summary>
    public void Begin(string? number)
    {
        Clear();

        if (string.IsNullOrWhiteSpace(number))
        {
            // Withheld, or an internal call with no caller id. There is nothing
            // to look up and "New customer" would be a lie.
            return;
        }

        var cts = new CancellationTokenSource();
        _lookup = cts;

        Set(() => State = Lookup.Searching);

        _ = LookUpAsync(number, cts.Token);
    }

    /// <summary>Forgets the last caller, so nothing carries into the next call.</summary>
    public void Clear()
    {
        var previous = _lookup;
        _lookup = null;

        try
        {
            previous?.Cancel();
            previous?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already gone; nothing to cancel.
        }

        Set(() =>
        {
            State = Lookup.None;
            Name = null;
            Address = null;
            Notes = null;
            IsVip = false;
            FlagReason = null;
            Orders = 0;
            Complaints = 0;
            Cancellations = 0;
        });
    }

    private async Task LookUpAsync(string number, CancellationToken ct)
    {
        try
        {
            var result = await _api.FindContactByPhoneAsync(number, ct);

            if (ct.IsCancellationRequested)
            {
                // The call this was for has gone. Dropping the answer is the
                // whole point: it belongs to a caller who is no longer here.
                return;
            }

            if (result.IsOk && result.Value is { } contact)
            {
                Show(contact);

                // The history and totals, once the name is already on screen.
                await LoadCardAsync(contact.Id, ct);
                return;
            }

            if (result.Status is ApiClient.ApiStatus.NotFound)
            {
                // The server looked and there is nobody. A-11.
                Set(() => State = Lookup.NewCustomer);
                return;
            }

            _logger.LogWarning(
                "The caller could not be identified: {Status} {Error}", result.Status, result.ErrorCode);

            Set(() => State = Lookup.Unknown);
        }
        catch (OperationCanceledException)
        {
            // The call ended while we were asking. Normal.
        }
        catch (Exception ex)
        {
            // Identifying the caller must never take the pop-up down. The agent
            // still has a ringing phone and a number on screen.
            _logger.LogError(ex, "Looking up the caller failed");
            Set(() => State = Lookup.Unknown);
        }
    }

    /// <summary>
    /// The totals (A-10). Failing here costs the pills and nothing else: the
    /// agent still has the customer's name in front of them, which is the part
    /// that changes what they say. The card also carries the last few calls;
    /// the pop-up stopped showing those (see DECISIONS, 2026-09-22) and
    /// ignores them here.
    /// </summary>
    private async Task LoadCardAsync(Guid contactId, CancellationToken ct)
    {
        var card = await _api.GetCallerCardAsync(contactId, ct: ct);

        if (ct.IsCancellationRequested || !card.IsOk || card.Value is not { } value)
        {
            if (!ct.IsCancellationRequested && !card.IsOk)
            {
                _logger.LogWarning(
                    "The caller's history could not be fetched: {Status}", card.Status);
            }

            return;
        }

        Set(() =>
        {
            Orders = value.Orders;
            Complaints = value.Complaints;
            Cancellations = value.Cancellations;
        });
    }

    private void Show(ContactDto contact) => Set(() =>
    {
        Name = contact.Name;
        Address = contact.Address;

        // The delivery note matters as much as the general one to somebody
        // taking an order, and there is no room for two labelled boxes.
        Notes = string.Join(
            " · ",
            new[] { contact.Notes, contact.DeliveryNotes }
                .Where(n => !string.IsNullOrWhiteSpace(n)));

        IsVip = contact.IsVip;
        FlagReason = contact.FlagReason;
        State = Lookup.Found;
    });

    private void Set(Action change) => _dispatcher.Invoke(change);
}
