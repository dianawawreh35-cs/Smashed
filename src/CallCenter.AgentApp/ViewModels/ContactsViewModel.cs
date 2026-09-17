using System.Collections.ObjectModel;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.Shared.Contracts.Contacts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// The shared contact list as the agent uses it (A-61, A-63).
/// </summary>
/// <remarks>
/// Agents are who create most contacts — usually mid-call, from a customer who
/// has just given their name. So the form is deliberately short: a name, a
/// number, an address, and the delivery note that saves the driver a phone call.
/// </remarks>
public partial class ContactsViewModel : ObservableObject
{
    private readonly ApiClient _api;

    /// <summary>The contact being edited, or null when creating a new one.</summary>
    private Guid? _editingId;

    public ContactsViewModel(ApiClient api, Localizer localizer)
    {
        _api = api;
        Localizer = localizer;

        Results.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));

        localizer.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(SameNameMessage));
        };
    }

    public Localizer Localizer { get; }

    public ObservableCollection<ContactSummaryDto> Results { get; } = [];

    /// <summary>Contacts already carrying the typed name — the warning in A-63.</summary>
    public ObservableCollection<ContactSummaryDto> SameName { get; } = [];

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusKey;

    [ObservableProperty]
    private bool _isEditing;

    // The form. Kept as separate fields rather than a bound DTO so a half-typed
    // contact is never mistaken for a saved one.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _deliveryNotes = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    public bool HasStatus => StatusKey is not null;

    /// <summary>
    /// Whether the grid has anything in it. Drives the empty state: a grid with
    /// no rows is a blank expanse that reads as a broken screen.
    /// </summary>
    public bool HasResults => Results.Count > 0;

    /// <summary>The status line, in the agent's language.</summary>
    public string? StatusMessage => StatusKey is null ? null : Localizer[StatusKey];

    /// <summary>How many contacts already carry this name, for the warning line.</summary>
    public string? SameNameMessage => SameName.Count == 0
        ? null
        : $"{Localizer["contacts.sameName"]} ({SameName.Count})";

    public bool CanSave => !IsBusy && !string.IsNullOrWhiteSpace(Phone);

    [RelayCommand]
    private async Task SearchAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusKey = null;

        try
        {
            var result = await _api.SearchContactsAsync(Query, ct);

            Results.Clear();

            if (!result.IsOk || result.Value is null)
            {
                // A-04: the app keeps working when the server is down, so this
                // says so rather than looking like an empty contact list.
                StatusKey = "contacts.offline";
                return;
            }

            foreach (var contact in result.Value)
            {
                Results.Add(contact);
            }

            if (Results.Count == 0)
            {
                StatusKey = "contacts.noMatches";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Opens the blank form for a new contact (A-11, A-63).</summary>
    [RelayCommand]
    private void New()
    {
        _editingId = null;
        Name = string.Empty;
        Phone = string.Empty;
        Address = string.Empty;
        DeliveryNotes = string.Empty;
        Notes = string.Empty;
        SameName.Clear();
        OnPropertyChanged(nameof(SameNameMessage));
        StatusKey = null;
        IsEditing = true;
    }

    /// <summary>Opens an existing contact for editing (A-63).</summary>
    [RelayCommand]
    private async Task EditAsync(ContactSummaryDto? summary)
    {
        if (summary is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var result = await _api.GetContactAsync(summary.Id);
            if (!result.IsOk || result.Value is null)
            {
                StatusKey = "contacts.offline";
                return;
            }

            var contact = result.Value;

            _editingId = contact.Id;
            Name = contact.Name ?? string.Empty;
            Phone = contact.Phones.FirstOrDefault()?.Raw ?? string.Empty;
            Address = contact.Address ?? string.Empty;
            DeliveryNotes = contact.DeliveryNotes ?? string.Empty;
            Notes = contact.Notes ?? string.Empty;

            SameName.Clear();
            OnPropertyChanged(nameof(SameNameMessage));
            StatusKey = null;
            IsEditing = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Looks up contacts already carrying the typed name (A-63). Called as the
    /// agent leaves the name box, so they are told before saving rather than
    /// after.
    /// </summary>
    [RelayCommand]
    private async Task CheckNameAsync()
    {
        SameName.Clear();
        OnPropertyChanged(nameof(SameNameMessage));

        if (string.IsNullOrWhiteSpace(Name))
        {
            return;
        }

        var result = await _api.FindContactsByNameAsync(Name, _editingId);
        if (!result.IsOk || result.Value is null)
        {
            return;
        }

        foreach (var match in result.Value)
        {
            SameName.Add(match);
        }

        OnPropertyChanged(nameof(SameNameMessage));
    }

    /// <summary>
    /// Adds the typed number to a contact that already has this name — what the
    /// agent chooses when the warning turns out to be the same person (A-63).
    /// </summary>
    [RelayCommand]
    private async Task AddToExistingAsync(ContactSummaryDto? existing)
    {
        if (existing is null || string.IsNullOrWhiteSpace(Phone))
        {
            return;
        }

        IsBusy = true;

        try
        {
            var result = await _api.AddContactPhoneAsync(existing.Id, Phone);
            StatusKey = result.IsOk ? "contacts.saved" : ErrorKey(result.ErrorCode);

            if (result.IsOk)
            {
                IsEditing = false;
                await SearchAsync(CancellationToken.None);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusKey = null;

        try
        {
            var request = new UpsertContactRequest(
                Blank(Name), Blank(Address), Blank(Notes), Blank(DeliveryNotes), [Phone.Trim()]);

            var result = _editingId is { } id
                ? await _api.UpdateContactAsync(id, request, ct)
                : await _api.CreateContactAsync(request, ct);

            if (!result.IsOk)
            {
                StatusKey = ErrorKey(result.ErrorCode);
                return;
            }

            StatusKey = "contacts.saved";
            IsEditing = false;
            await SearchAsync(ct);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        IsEditing = false;
        StatusKey = null;
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Turns the server's refusal code into a label key. An unknown code falls
    /// back to the general message rather than showing the raw code.
    /// </summary>
    private static string ErrorKey(string? code) => code switch
    {
        "duplicate_number" => "contacts.errors.duplicate_number",
        "no_usable_number" => "contacts.errors.no_usable_number",
        "contact_not_found" => "contacts.errors.contact_not_found",
        "server_unreachable" => "contacts.offline",
        _ => "contacts.errors.server_error",
    };
}
