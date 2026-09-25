using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.Shared.Contracts.Communications;
using CallCenter.Shared.Phone;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// Applications: the conversations that reach the restaurant on WhatsApp,
/// Facebook, Instagram or Wheels rather than by phone (A-70 to A-73). The
/// agent records one here, and sees their own (A-71).
/// </summary>
/// <remarks>
/// <b>A message is recorded, never queued.</b> A call is logged by the app as
/// it ends, whether or not the server is there (A-04), because nobody typed
/// it. A message is typed by the agent, who can wait: with the server down the
/// screen says so and keeps what was typed (Dia, 25 Sep). Nothing here touches
/// the offline buffer.
///
/// <b>The customer is found the way the pop-up finds one.</b> The number box
/// is the quick entry (A-73): once the agent pauses typing, the number goes to
/// the same lookup the pop-up uses, and a number nobody has offers the same
/// "Save as new customer" form (A-11). That is <see cref="CallerViewModel"/>
/// reused, not rewritten - its own instance, because the registered one
/// belongs to the pop-up, and a message's customer must not appear on the
/// next call.
///
/// <b>The classification form is the call's form</b>, drawn from the third
/// definition, "Applications", and read rather than saved: the message does
/// not exist until the server has it, so the answers travel in the same
/// request (A-70). Opening a recorded message uses the form the usual way,
/// against the message's id (A-71).
///
/// <b>Every filter on the list is applied by the server</b>, as the call log's
/// are (see the 20 Sep entry "filtering a page lies").
/// </remarks>
public partial class ApplicationsViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// How long the typing has to stop before the server is asked - for the
    /// customer lookup and for the list's search alike.
    /// </summary>
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Fewer digits than this is not a number yet, and asking the server about
    /// "05" would answer "new customer" for every customer in the country.
    /// </summary>
    private const int MinimumDigitsToLookUp = 6;

    private readonly ApiClient _api;
    private readonly ILogger<ApplicationsViewModel> _logger;
    private readonly DispatcherTimer _lookupTimer;
    private readonly DispatcherTimer _searchTimer;

    /// <summary>Cancels a list fetch that a newer one has replaced.</summary>
    private CancellationTokenSource? _inFlight;

    /// <summary>
    /// The branch of the last message recorded, pre-selected on the next
    /// (A-73). Per session: it goes when the app does.
    /// </summary>
    private Guid? _lastBranchId;

    public ApplicationsViewModel(
        ApiClient api,
        ClassificationFormViewModel classification,
        ClassificationFormViewModel openedClassification,
        Localizer localizer,
        Dispatcher dispatcher,
        IServiceProvider services,
        ILogger<ApplicationsViewModel> logger)
    {
        _api = api;
        _logger = logger;
        Localizer = localizer;
        Classification = classification;
        OpenedClassification = openedClassification;

        // A-11: the same lookup and new-customer form as the pop-up, on its own
        // instance. The one in DI is a singleton and is the pop-up's; sharing
        // it would put this screen's customer on the next incoming call.
        Customer = ActivatorUtilities.CreateInstance<CallerViewModel>(services);

        // A saved classification on an opened message clears its chip, so the
        // list is refetched rather than guessed at.
        OpenedClassification.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ClassificationFormViewModel.IsSaved)
                && OpenedClassification.IsSaved)
            {
                _ = RefreshAsync(CancellationToken.None);
            }
        };

        _lookupTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TypingPause };
        _lookupTimer.Tick += (_, _) =>
        {
            _lookupTimer.Stop();
            LookUpCustomer();
        };

        _searchTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TypingPause };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            _ = RefreshAsync(CancellationToken.None);
        };

        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMessages));

        localizer.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(RecordMessage));
            OnPropertyChanged(nameof(EditMessage));
            OnPropertyChanged(nameof(FormUnavailable));
            Rebuild();
            OnPropertyChanged(nameof(Opened));

            // The form's fields keep both languages and only need telling.
            foreach (var field in Classification.Fields.Concat(OpenedClassification.Fields))
            {
                field.Retranslate();
            }
        };

        ResetTime();
        FormLoaded = Classification.BeginForApplication();
    }

    public Localizer Localizer { get; }

    // ---- recording a message (A-70) ------------------------------------------

    /// <summary>The apps a customer can write on (S-41). Phone is left out: a phone conversation is a call.</summary>
    public ObservableCollection<ChannelOption> Channels { get; } = [];

    /// <summary>
    /// Which app. Kept after a message is recorded (A-73): an agent working
    /// WhatsApp records ten in a row, and picking it ten times is the kind of
    /// thing that makes a screen go unused.
    /// </summary>
    [ObservableProperty]
    private ChannelOption? _selectedChannel;

    /// <summary>
    /// The customer's number, as typed. The quick entry (A-73): pausing after
    /// typing it looks the customer up, as the pop-up does for a caller.
    /// </summary>
    [ObservableProperty]
    private string _number = string.Empty;

    /// <summary>
    /// When the customer wrote, as <c>HH:mm</c>. Defaults to now; an agent may
    /// set it back within today, and the server refuses anything else.
    /// </summary>
    [ObservableProperty]
    private string _timeText = string.Empty;

    /// <summary>Who the number belongs to, and the new-customer form (A-11).</summary>
    public CallerViewModel Customer { get; }

    /// <summary>
    /// The classification of the message being recorded, from the Applications
    /// form. Read when the message is sent, never saved on its own.
    /// </summary>
    public ClassificationFormViewModel Classification { get; }

    /// <summary>
    /// Whether the Applications form reached the app at sign-in. Without it a
    /// message cannot be recorded, since it would have no type, and the screen
    /// says why.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormUnavailable))]
    private bool _formLoaded;

    public string? FormUnavailable => FormLoaded ? null : Localizer["applications.formUnavailable"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecordCommand))]
    private bool _isRecording;

    /// <summary>A confirmation, or why the message was not recorded. A key, so it follows the language.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordMessage))]
    private string? _recordMessageKey;

    /// <summary>
    /// True when <see cref="RecordMessageKey"/> is a refusal rather than a
    /// confirmation, so the screen can colour it as one.
    /// </summary>
    [ObservableProperty]
    private bool _recordFailed;

    public string? RecordMessage => RecordMessageKey is null ? null : Localizer[RecordMessageKey];

    partial void OnNumberChanged(string value)
    {
        RecordMessageKey = null;

        // Typing restarts the clock, so a whole number costs one lookup rather
        // than one per digit (A-73).
        _lookupTimer.Stop();

        if (PhoneNormalizer.DigitsOnly(value).Length < MinimumDigitsToLookUp)
        {
            // Not a number yet. Whatever the last lookup said is about a
            // number that is no longer in the box.
            Customer.Clear();
            return;
        }

        _lookupTimer.Start();
    }

    /// <summary>
    /// Finds who the typed number belongs to (A-73). The number goes as typed:
    /// the server puts it through <c>PhoneNormalizer</c> and matches on the
    /// last nine digits (A-13), exactly as it does for a caller.
    /// </summary>
    private void LookUpCustomer()
    {
        var number = Number.Trim();

        if (PhoneNormalizer.DigitsOnly(number).Length < MinimumDigitsToLookUp)
        {
            Customer.Clear();
            return;
        }

        Customer.Begin(number);
    }

    private bool CanRecord => !IsRecording;

    /// <summary>
    /// Sends the message with its classification (A-70). The type is required
    /// and the form must be complete: both are refused here, before the server
    /// sees them, with what is missing named. A message is never recorded
    /// unclassified (Dia, 25 Sep).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRecord))]
    private async Task RecordAsync()
    {
        RecordFailed = true;

        if (SelectedChannel is null)
        {
            RecordMessageKey = "applications.errors.noChannel";
            return;
        }

        var number = Number.Trim();

        if (PhoneNormalizer.DigitsOnly(number).Length == 0 && Customer.ContactId is null)
        {
            RecordMessageKey = "applications.errors.no_customer";
            return;
        }

        if (TimeToday(TimeText) is not { } startedAt)
        {
            RecordMessageKey = "applications.errors.badTimeFormat";
            return;
        }

        // Dia, 25 Sep: a message is always recorded with its type. Unlike a
        // call, which happens to the agent and may be skipped (A-41), the agent
        // has just read it and knows what it was. The server refuses one
        // without it too (classification_required).
        if (!FormLoaded)
        {
            // Nothing to classify with, so nothing is sent: a message typed by
            // hand can wait for the form, as it waits for the server (A-70).
            RecordMessageKey = "applications.formUnavailable";
            return;
        }

        if (!Classification.HasType)
        {
            RecordMessageKey = "applications.errors.typeRequired";
            return;
        }

        if (!Classification.IsComplete)
        {
            // The form says what is missing, under its fields.
            RecordMessageKey = "applications.errors.formIncomplete";
            return;
        }

        IsRecording = true;
        RecordMessageKey = null;

        try
        {
            var request = new RecordApplicationRequest(
                ChannelId: SelectedChannel.Channel.Id,
                RemoteNumber: number.Length == 0 ? null : number,
                ContactId: Customer.ContactId,
                StartedAt: startedAt,
                Classification: Classification.BuildRequest(),
                LaptopId: LaptopInfo.LaptopId);

            var result = await _api.RecordApplicationAsync(request);

            if (!result.IsOk)
            {
                // What was typed stays on screen: the agent fixes the one thing
                // named and presses Record again.
                RecordMessageKey = result.Status == ApiClient.ApiStatus.Unreachable
                    ? "applications.errors.offline"
                    : ErrorKey(result.ErrorCode);
                return;
            }

            RecordFailed = false;
            RecordMessageKey = "applications.recorded";

            // A-73: the branch is remembered for the next message; the channel
            // simply stays selected.
            _lastBranchId = Classification.Fields.OfType<BranchFieldViewModel>()
                .FirstOrDefault()?.Selected?.Branch.Id ?? _lastBranchId;

            StartNext();
            _ = RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A message could not be recorded");
            RecordMessageKey = "classification.saveFailed";
        }
        finally
        {
            IsRecording = false;
        }
    }

    /// <summary>Drops what was typed and starts a blank message. The channel stays (A-73).</summary>
    [RelayCommand]
    private void ClearForm()
    {
        RecordMessageKey = null;
        StartNext();
    }

    /// <summary>
    /// A blank message: number cleared, time back to now, the form redrawn
    /// with the remembered branch pre-selected (A-73).
    /// </summary>
    private void StartNext()
    {
        _lookupTimer.Stop();
        Number = string.Empty;
        Customer.Clear();
        ResetTime();

        FormLoaded = Classification.BeginForApplication();

        if (_lastBranchId is { } branchId)
        {
            Classification.Fields.OfType<BranchFieldViewModel>().FirstOrDefault()?.Select(branchId);
        }
    }

    private void ResetTime() => TimeText = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// The typed time on today's date, or null when it cannot be read. Today
    /// only: an agent's message is from today by the SRS (A-70), and the
    /// server refuses anything else with <c>bad_time</c>.
    /// </summary>
    private static DateTimeOffset? TimeToday(string text) =>
        TimeOn(DateTime.Today, text);

    private static DateTimeOffset? TimeOn(DateTime date, string text)
    {
        // Arabic-Indic digits are typed on Arabic keyboards; the number box
        // already folds them, and the time box should not be pickier.
        var digits = PhoneNormalizer.DigitsOnly(text);
        var withColon = text.Trim().Replace('٫', ':').Replace('.', ':');

        if (TimeOnly.TryParseExact(withColon, ["H:mm", "HH:mm", "H:m"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time)
            || (digits.Length == 4 && TimeOnly.TryParseExact(digits, "HHmm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out time)))
        {
            // Unspecified kind, so the offset is this laptop's: the server
            // compares against its own local day.
            return new DateTimeOffset(DateTime.SpecifyKind(date.Date.Add(time.ToTimeSpan()), DateTimeKind.Local));
        }

        return null;
    }

    /// <summary>The server's refusal, as a label key the agent can act on.</summary>
    private static string ErrorKey(string? code) => code switch
    {
        "unknown_channel" => "applications.errors.unknown_channel",
        "phone_channel" => "applications.errors.phone_channel",
        "no_customer" => "applications.errors.no_customer",
        "unknown_contact" => "applications.errors.unknown_contact",
        "bad_time" => "applications.errors.bad_time",
        "unknown_type" or "unknown_branch" or "classification_refused" => "applications.errors.classification_refused",
        "message_not_found" => "applications.errors.message_not_found",
        "classification_required" => "applications.errors.typeRequired",
        "type_not_offered" => "classification.typeNotOffered",
        "edit_window_closed" => "classification.tooOld",
        "not_your_call" => "classification.notYours",
        _ => "classification.saveFailed",
    };

    // ---- the agent's own messages (A-71) -------------------------------------

    public ObservableCollection<MessageRow> Messages { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusKey;

    /// <summary>Filters by number or customer name. Sent once typing pauses.</summary>
    [ObservableProperty]
    private string _query = string.Empty;

    /// <summary>Earliest message to show. Null for no lower bound.</summary>
    [ObservableProperty]
    private DateTime? _from;

    /// <summary>Latest day to show, included whole. Null for no upper bound.</summary>
    [ObservableProperty]
    private DateTime? _to;

    public bool HasStatus => StatusKey is not null;

    public bool HasMessages => Messages.Count > 0;

    public string? StatusMessage => StatusKey is null ? null : Localizer[StatusKey];

    public bool HasFilters => From is not null || To is not null || Query.Trim().Length > 0;

    partial void OnQueryChanged(string value)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    partial void OnFromChanged(DateTime? value) => _ = RefreshAsync(CancellationToken.None);

    partial void OnToChanged(DateTime? value) => _ = RefreshAsync(CancellationToken.None);

    [RelayCommand]
    private void ClearFilters()
    {
        _searchTimer.Stop();
        Query = string.Empty;
        From = null;
        To = null;
        _ = RefreshAsync(CancellationToken.None);
    }

    /// <summary>Everything the last fetch returned, kept only to re-render labels.</summary>
    private IReadOnlyList<CommunicationDto> _fetched = [];

    /// <summary>
    /// The list, and the channels the first time. Both from the server, the
    /// channels because the supervisor manages them (S-41) and a new app must
    /// reach agents without a reinstall.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        // A newer request replaces an older one, so a slow answer for "05"
        // cannot land after the answer for "0599".
        var previous = _inFlight;
        var current = new CancellationTokenSource();
        var token = current.Token;
        _inFlight = current;

        // Cancelled and not disposed: the run that owns it is still looking at
        // its own token (see CallLogViewModel for the incident).
        previous?.Cancel();

        IsBusy = true;
        StatusKey = null;
        OnPropertyChanged(nameof(HasFilters));

        try
        {
            if (Channels.Count == 0)
            {
                await LoadChannelsAsync(token);
            }

            // The form missed at sign-in is tried again on Refresh, so the
            // agent has a way back short of signing out.
            if (!FormLoaded)
            {
                FormLoaded = Classification.BeginForApplication();
            }

            var result = await _api.GetMyApplicationsAsync(
                from: From is { } from ? new DateTimeOffset(from.Date) : null,
                to: To is { } to ? new DateTimeOffset(to.Date) : null,
                query: Query,
                ct: token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsOk || result.Value is null)
            {
                _fetched = [];
                Messages.Clear();
                StatusKey = "applications.offline";
                return;
            }

            _fetched = result.Value;
            Rebuild();

            StatusKey = Messages.Count > 0
                ? null
                : HasFilters ? "applications.noMatches" : "applications.empty";
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

    private async Task LoadChannelsAsync(CancellationToken ct)
    {
        var result = await _api.GetChannelsAsync(ct);

        if (ct.IsCancellationRequested || !result.IsOk || result.Value is null)
        {
            return;
        }

        Channels.Clear();

        // A-70: never Phone. A phone conversation is a call and is logged by
        // the app as it ends; offering Phone here would invite a second copy.
        foreach (var channel in result.Value.Where(c => !c.IsSystem && c.IsActive).OrderBy(c => c.SortOrder))
        {
            Channels.Add(new ChannelOption(channel));
        }

        // The remembered channel (A-73) survives a reload of the list; a new
        // session starts on the first one, so the box is never empty.
        SelectedChannel = Channels.FirstOrDefault(c => c.Channel.Id == SelectedChannel?.Channel.Id)
                          ?? Channels.FirstOrDefault();
    }

    private void Rebuild()
    {
        Messages.Clear();

        foreach (var message in _fetched)
        {
            Messages.Add(new MessageRow(message, Localizer));
        }

        // The open message keeps pointing at the row that was opened, so its
        // chip and details follow a refresh.
        if (Opened is { } opened)
        {
            Opened = Messages.FirstOrDefault(m => m.Id == opened.Id) ?? opened;
        }
    }

    // ---- an opened message (A-71) ------------------------------------------

    /// <summary>The message opened from the list, or null when none is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    private MessageRow? _opened;

    public bool IsOpen => Opened is not null;

    /// <summary>The form for the opened message, prefilled with what it says.</summary>
    public ClassificationFormViewModel OpenedClassification { get; }

    /// <summary>The opened message's channel, customer number and time, as edited.</summary>
    [ObservableProperty]
    private ChannelOption? _editChannel;

    [ObservableProperty]
    private string _editNumber = string.Empty;

    [ObservableProperty]
    private string _editTimeText = string.Empty;

    /// <summary>
    /// Today's messages are editable, older ones read-only (A-71). The server
    /// enforces its window with <c>edit_window_closed</c>; this greys the
    /// fields out so the agent is told before pressing Save, not after.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    private bool _canEditOpened;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    private bool _isSavingEdit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditMessage))]
    private string? _editMessageKey;

    public string? EditMessage => EditMessageKey is null ? null : Localizer[EditMessageKey];

    /// <summary>
    /// Opens a message from the list: its details, editable for today's, and
    /// its classification form prefilled (A-71). Shown under the list, so the
    /// list itself never moves.
    /// </summary>
    [RelayCommand]
    private async Task OpenAsync(MessageRow? row)
    {
        if (row is null)
        {
            return;
        }

        Opened = row;
        EditMessageKey = null;
        EditChannel = Channels.FirstOrDefault(c => c.Name == row.Channel);
        EditNumber = row.Number;
        EditTimeText = row.Time;
        CanEditOpened = row.IsToday;

        await OpenedClassification.BeginForLoggedApplicationAsync(row.Id);
    }

    private bool CanSaveEdit => CanEditOpened && !IsSavingEdit;

    /// <summary>Saves the channel, number and time of the opened message (A-71).</summary>
    [RelayCommand(CanExecute = nameof(CanSaveEdit))]
    private async Task SaveEditAsync()
    {
        if (Opened is not { } row)
        {
            return;
        }

        if (EditChannel is null)
        {
            EditMessageKey = "applications.errors.noChannel";
            return;
        }

        var number = EditNumber.Trim();

        if (PhoneNormalizer.DigitsOnly(number).Length == 0 && row.ContactId is null)
        {
            EditMessageKey = "applications.errors.no_customer";
            return;
        }

        // The message's own date, only the time changes: the day it was
        // written is not the agent's to move (A-70).
        if (TimeOn(row.StartedAtLocal.Date, EditTimeText) is not { } startedAt)
        {
            EditMessageKey = "applications.errors.badTimeFormat";
            return;
        }

        IsSavingEdit = true;
        EditMessageKey = null;

        try
        {
            // The contact is left for the server to match from the number when
            // the number changed; sent as it was when it did not, so a message
            // recorded against a chosen contact keeps them.
            var result = await _api.EditApplicationAsync(row.Id, new EditApplicationRequest(
                ChannelId: EditChannel.Channel.Id,
                RemoteNumber: number.Length == 0 ? null : number,
                ContactId: number == row.Number ? row.ContactId : null,
                StartedAt: startedAt));

            if (!result.IsOk)
            {
                EditMessageKey = result.Status == ApiClient.ApiStatus.Unreachable
                    ? "applications.errors.offline"
                    : ErrorKey(result.ErrorCode);
                return;
            }

            EditMessageKey = "applications.edited";
            _ = RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A message could not be edited");
            EditMessageKey = "classification.saveFailed";
        }
        finally
        {
            IsSavingEdit = false;
        }
    }

    /// <summary>Closes the open message: its details and its form together.</summary>
    [RelayCommand]
    private void CloseMessage()
    {
        OpenedClassification.Close();
        Opened = null;
        EditMessageKey = null;
    }

    public void Dispose()
    {
        _lookupTimer.Stop();
        _searchTimer.Stop();

        try
        {
            _inFlight?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}

/// <summary>
/// One channel, as the list shows it. A wrapper rather than the DTO, for the
/// reason <see cref="BranchOption"/> gives: a record prints as its contents.
/// </summary>
public class ChannelOption(ChannelDto channel)
{
    public ChannelDto Channel { get; } = channel;

    public string Name => Channel.Name;

    public override string ToString() => Name;
}

/// <summary>One message, ready for the grid (A-71).</summary>
public class MessageRow(CommunicationDto message, Localizer localizer)
{
    public Guid Id => message.Id;

    public Guid? ContactId => message.ContactId;

    /// <summary>When it was written, in this laptop's time.</summary>
    public DateTimeOffset StartedAtLocal => message.StartedAt.ToLocalTime();

    /// <summary>
    /// The time for a message from today, the date as well for an older one -
    /// the same rule as the call log, for the same reason.
    /// </summary>
    public string When => IsToday
        ? StartedAtLocal.ToString("HH:mm", CultureInfo.InvariantCulture)
        : StartedAtLocal.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The time alone, for the edit box.</summary>
    public string Time => StartedAtLocal.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Date and time in full, for the details of an opened message.</summary>
    public string StartedAt => StartedAtLocal.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Written today, so the agent may still edit it (A-71).</summary>
    public bool IsToday => StartedAtLocal.Date == DateTimeOffset.Now.Date;

    /// <summary>Which app it came on (A-72).</summary>
    public string Channel => message.ChannelName ?? string.Empty;

    /// <summary>
    /// The customer's name, or "Not in contacts" when nobody is on file. Not the
    /// number: it has its own column beside this one, and printing it twice in
    /// one row was half of what made the list read as cramped (25 Sep).
    /// </summary>
    public string Who => string.IsNullOrWhiteSpace(message.ContactName)
        ? localizer["applications.notInContacts"]
        : message.ContactName!;

    /// <summary>No contact: the name column is dimmed, so a real name stands out.</summary>
    public bool IsUnknown => string.IsNullOrWhiteSpace(message.ContactName);

    public string Number => message.RemoteNumberRaw ?? string.Empty;

    /// <summary>
    /// Classified or not, as a chip. The list carries no type - the DTO has
    /// only the flag - and the type shows in the form when the row is opened.
    /// </summary>
    public bool IsUnclassified => message.IsUnclassified;

    public bool IsClassified => message.IsClassified;

    public string Notes => message.Notes ?? string.Empty;

    public bool HasNotes => !string.IsNullOrWhiteSpace(message.Notes);
}
