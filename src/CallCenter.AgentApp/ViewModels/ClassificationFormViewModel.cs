using System.Collections.ObjectModel;
using System.Text.Json;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Calls;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.Shared.Contracts.Classifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// The classification form, as the agent fills it in during a call (A-40).
/// </summary>
/// <remarks>
/// <b>It opens when the call is answered, not at hang-up.</b> The agent is
/// taking the order while the customer is speaking — the order value, the
/// branch and the notes are the call, not a summary of it. A form appearing
/// after the customer has gone asks the agent to remember what they were just
/// told.
///
/// <b>Saving is always queued</b> (see <see cref="CallLogReporter.ClassifyAsync"/>).
/// The call itself is not reported until it ends, so while this form is on
/// screen the server has never heard of the call. The queue keeps the
/// classification behind it. From here that is invisible: save, and it is dealt
/// with.
///
/// <b>Nothing is stored until the agent saves</b> (A-41). A form left untouched
/// when the call ends is a skip, and the call is simply unclassified — which the
/// call log then highlights until somebody deals with it.
/// </remarks>
public partial class ClassificationFormViewModel(
    ApiClient api,
    CallLogReporter reporter,
    AgentSession session,
    Localizer localizer,
    ILogger<ClassificationFormViewModel> logger) : ObservableObject
{
    private ClassificationFormDto? _form;
    private string? _sipCallId;
    private string? _extension;

    public Localizer Localizer { get; } = localizer;

    /// <summary>The fields to draw, in the order the supervisor put them.</summary>
    public ObservableCollection<ClassificationFieldViewModel> Fields { get; } = [];

    /// <summary>Whether the form should be on screen at all.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnfinished))]
    private bool _isOpen;

    /// <summary>The agent has saved, so the call is classified.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnfinished))]
    private bool _isSaved;

    [ObservableProperty]
    private bool _isSaving;

    /// <summary>Shown under the buttons — a confirmation or a reason it cannot be saved.</summary>
    [ObservableProperty]
    private string _message = string.Empty;

    /// <summary>
    /// Open, and not yet dealt with. The pop-up stays up while this is true even
    /// after the call has ended, so a call that finishes mid-sentence does not
    /// take the agent's typing with it.
    /// </summary>
    public bool IsUnfinished => IsOpen && !IsSaved;

    /// <summary>
    /// Fetches the form (A-40, S-40).
    /// </summary>
    /// <remarks>
    /// At sign-in, so the fields are in hand before the first call rather than
    /// being fetched while an agent waits. A supervisor's change reaches agents
    /// at the next sign-in without anything being reinstalled.
    /// </remarks>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var result = await api.GetClassificationFormAsync(ct);

        if (result.IsOk && result.Value is { } form)
        {
            _form = form;
            logger.LogInformation("Classification form version {Version} loaded", form.Version);
            return;
        }

        logger.LogWarning(
            "The classification form could not be loaded ({Code}); classifying is unavailable",
            result.ErrorCode);
    }

    /// <summary>
    /// Starts a blank form for a call that has just been answered (A-40).
    /// </summary>
    /// <remarks>
    /// Keyed on the SIP Call-ID and the extension rather than a server id,
    /// because the server has no record of the call yet — it is told when the
    /// call ends.
    /// </remarks>
    public void Begin(string? sipCallId, string? extension)
    {
        if (_form is null || string.IsNullOrWhiteSpace(sipCallId) || string.IsNullOrWhiteSpace(extension))
        {
            // Without the form or the key there is nothing to draw and nowhere
            // to send it. The call still works; it is simply unclassified.
            logger.LogWarning(
                "A call was answered but cannot be classified (form loaded: {HasForm}, call id: {HasCallId})",
                _form is not null, !string.IsNullOrWhiteSpace(sipCallId));

            IsOpen = false;
            return;
        }

        _sipCallId = sipCallId;
        _extension = extension;

        Build();

        IsSaved = false;
        IsSaving = false;
        Message = string.Empty;
        IsOpen = true;
    }

    /// <summary>Puts the form away, saved or skipped.</summary>
    public void Close()
    {
        IsOpen = false;
        Fields.Clear();
        _sipCallId = null;
        _extension = null;
    }

    private void Build()
    {
        Fields.Clear();

        if (_form is null)
        {
            return;
        }

        foreach (var field in _form.Definition.RootElement.GetProperty("fields").EnumerateArray())
        {
            var kind = field.TryGetProperty("kind", out var k) ? k.GetString() : null;

            ClassificationFieldViewModel? built = kind switch
            {
                "type" => new TypeFieldViewModel(field, Localizer, _form.Types),
                "branch" => new BranchFieldViewModel(field, Localizer, _form.Branches),
                "text" => new TextFieldViewModel(field, Localizer),
                "textarea" => new TextAreaFieldViewModel(field, Localizer),
                "number" => new NumberFieldViewModel(field, Localizer),
                "select" => new SelectFieldViewModel(field, Localizer),
                "checkbox" => new CheckboxFieldViewModel(field, Localizer),

                // A kind this version of the app does not know. Skipped rather
                // than crashing the form: a supervisor on a newer server must
                // not be able to stop every agent classifying.
                _ => null,
            };

            if (built is null)
            {
                logger.LogWarning("The form has a field kind this app cannot draw: {Kind}", kind);
                continue;
            }

            Fields.Add(built);
        }

        // Every field, not just the type. Whether the form can be saved depends
        // on all of them - the branch is required too - so listening only to the
        // type left Save greyed out after the agent had filled everything in,
        // with nothing on screen saying why.
        foreach (var field in Fields)
        {
            field.PropertyChanged += (_, _) =>
            {
                SaveCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(Missing));
            };
        }

        // The type additionally decides which other fields are asked at all.
        if (Fields.OfType<TypeFieldViewModel>().FirstOrDefault() is { } type)
        {
            type.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TypeFieldViewModel.Selected))
                {
                    ApplyVisibility(type.SelectedName);
                }
            };
        }

        ApplyVisibility(null);
    }

    /// <summary>
    /// Shows the fields that apply to the chosen type and hides the rest.
    /// </summary>
    /// <remarks>
    /// Matched on the type's <b>name</b>, never its label. A supervisor renaming
    /// "Complaint" to "Issue" must not make the complaint-reason field vanish.
    /// </remarks>
    private void ApplyVisibility(string? typeName)
    {
        foreach (var field in Fields)
        {
            field.AppliesNow = field.ShowWhenType.Count == 0
                              || (typeName is not null && field.ShowWhenType.Contains(typeName));
        }
    }

    private bool CanSave =>
        !IsSaving
        && Fields.OfType<TypeFieldViewModel>().FirstOrDefault()?.Selected is not null
        && Fields.Where(f => f.AppliesNow).All(f => f.IsSatisfied);

    /// <summary>
    /// What is still missing, named, or empty when the form is ready.
    /// </summary>
    /// <remarks>
    /// A disabled Save button with no explanation is the worst version of this:
    /// the agent has a customer on the line and no idea what the screen wants.
    /// </remarks>
    public string Missing
    {
        get
        {
            if (CanSave || Fields.Count == 0)
            {
                return string.Empty;
            }

            var names = Fields
                .Where(f => f.AppliesNow && !f.IsSatisfied)
                .Select(f => f.Label)
                .ToList();

            return names.Count == 0
                ? string.Empty
                : $"{Localizer["classification.stillNeeded"]} {string.Join("، ", names)}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_sipCallId is null || _extension is null || _form is null)
        {
            return;
        }

        IsSaving = true;
        Message = string.Empty;

        try
        {
            var type = Fields.OfType<TypeFieldViewModel>().First();
            var branch = Fields.OfType<BranchFieldViewModel>().FirstOrDefault();

            // The built-in fields have their own columns because the reports
            // group by them; everything the supervisor added travels together.
            var custom = Fields
                .Where(f => f.AppliesNow)
                .Where(f => f is not TypeFieldViewModel and not BranchFieldViewModel)
                .Where(f => f.Key is not ("order_value" or "notes" or "follow_up"))
                .Where(f => f.Value is not null)
                .ToDictionary(f => f.Key, f => f.Value!);

            var request = new SaveClassificationByCallRequest(
                _sipCallId,
                _extension,
                new SaveClassificationRequest(
                    TypeId: type.Selected!.Type.Id,
                    BranchId: branch?.Selected?.Branch.Id,
                    OrderValue: FieldValue("order_value") as decimal?,
                    Notes: FieldValue("notes") as string,
                    FollowUp: FieldValue("follow_up") as bool? ?? false,
                    Resolved: null,
                    FormVersion: _form.Version,
                    CustomValues: JsonSerializer.SerializeToDocument(custom)));

            await reporter.ClassifyAsync(request);

            IsSaved = true;
            Message = Localizer["classification.saved"];
        }
        catch (Exception ex)
        {
            // Queuing is meant never to throw, so this is a surprise rather than
            // an expected path - but losing what an agent typed because
            // something unexpected happened would be the worst outcome here.
            logger.LogError(ex, "A classification could not be saved");
            Message = Localizer["classification.saveFailed"];
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Leaves the call unclassified (A-41).
    /// </summary>
    /// <remarks>
    /// Not a failure: an agent between calls has better things to do, and the
    /// call log highlights it until somebody deals with it.
    /// </remarks>
    [RelayCommand]
    private void Skip()
    {
        logger.LogInformation("A call was left unclassified by the agent");
        Close();
    }

    private object? FieldValue(string key) =>
        Fields.FirstOrDefault(f => f.Key == key && f.AppliesNow)?.Value;

    /// <summary>The agent's extension, for keying the classification.</summary>
    public string? Extension => session.Extensions?.Extension;
}
