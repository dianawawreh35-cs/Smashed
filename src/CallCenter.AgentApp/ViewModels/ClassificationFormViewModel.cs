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
    ClassificationCatalog catalog,
    CallLogReporter reporter,
    AgentSession session,
    Localizer localizer,
    ILogger<ClassificationFormViewModel> logger) : ObservableObject
{
    private string? _sipCallId;
    private string? _extension;

    /// <summary>
    /// Set when classifying a call from the log rather than one in progress.
    /// </summary>
    /// <remarks>
    /// The two paths differ in what they can key on. A call in progress has no
    /// server id yet, so it is keyed on the SIP Call-ID and queued behind the
    /// call. A call in the log came from the server, so its id is known and the
    /// classification goes straight there.
    /// </remarks>
    private Guid? _communicationId;

    private ClassificationFormDto? _form => catalog.Form;

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

    /// <summary>
    /// The call can be read but not changed (A-42) — past the agent's window, or
    /// another agent's call.
    /// </summary>
    [ObservableProperty]
    private bool _isReadOnly;

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
        _communicationId = null;

        Build();

        IsSaved = false;
        IsSaving = false;
        Message = string.Empty;
        IsOpen = true;
    }

    /// <summary>
    /// Opens a blank form for a call already on the server (A-41, A-42).
    /// </summary>
    /// <remarks>
    /// The way back to a call the agent skipped. Without it, "the agent may
    /// skip" means "the call is unclassified for ever", and the chip in the call
    /// log points at work nobody can do.
    ///
    /// The server decides whether it is allowed: an agent may edit their own
    /// calls within the window the supervisor set, and gets a plain refusal
    /// otherwise rather than a form that will not save.
    /// </remarks>
    public async Task BeginForLoggedCallAsync(Guid communicationId, CancellationToken ct = default)
    {
        if (_form is null)
        {
            logger.LogWarning("A logged call cannot be classified: the form was never loaded");
            IsOpen = false;
            return;
        }

        _communicationId = communicationId;
        _sipCallId = null;
        _extension = null;

        Build();

        IsSaved = false;
        IsSaving = false;
        IsReadOnly = false;
        Message = string.Empty;
        IsOpen = true;

        // What the call already says, if anything. Opening blank over an
        // existing classification is not just unhelpful - saving it would
        // replace a real answer with nothing, and the agent would never know.
        var existing = await api.GetClassificationAsync(communicationId, ct);

        if (existing.IsOk && existing.Value is { } saved)
        {
            Prefill(saved);

            if (!saved.CanEdit)
            {
                // A-42: past the window, or another agent's call. Shown rather
                // than discovered by pressing Save.
                IsReadOnly = true;
                Message = Localizer["classification.tooOld"];
            }
        }

        SaveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Missing));
    }

    /// <summary>
    /// Puts an existing classification back on screen (A-42).
    /// </summary>
    /// <remarks>
    /// The built-in answers have their own columns; everything the supervisor
    /// added is in <c>customValues</c>, keyed by field key. A key with no field
    /// is skipped: the form may have changed since, and an answer to a question
    /// nobody asks any more has nowhere to go.
    /// </remarks>
    private void Prefill(ClassificationDto saved)
    {
        foreach (var field in Fields)
        {
            switch (field)
            {
                case TypeFieldViewModel type:
                    type.Select(saved.TypeId, _form!.Types, Localizer);
                    break;

                case BranchFieldViewModel branch when saved.BranchId is { } branchId:
                    branch.Select(branchId);
                    break;

                case NumberFieldViewModel number when field.Key == "order_value":
                    number.Text = saved.OrderValue?.ToString() ?? string.Empty;
                    break;

                case TextAreaFieldViewModel notes when field.Key == "notes":
                    notes.Text = saved.Notes ?? string.Empty;
                    break;

                case CheckboxFieldViewModel followUp when field.Key == "follow_up":
                    followUp.IsChecked = saved.FollowUp;
                    break;

                default:
                    if (saved.CustomValues.RootElement.ValueKind == JsonValueKind.Object
                        && saved.CustomValues.RootElement.TryGetProperty(field.Key, out var value))
                    {
                        field.Prefill(value);
                    }

                    break;
            }
        }

        ApplyVisibility(Fields.OfType<TypeFieldViewModel>().FirstOrDefault()?.SelectedName);
    }

    /// <summary>Puts the form away, saved or skipped.</summary>
    public void Close()
    {
        IsOpen = false;
        Fields.Clear();
        _sipCallId = null;
        _extension = null;
        _communicationId = null;
        IsReadOnly = false;
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
        && !IsReadOnly
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
        if (_form is null || (_communicationId is null && (_sipCallId is null || _extension is null)))
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

            if (_communicationId is { } communicationId)
            {
                // A call from the log: the server knows it, so this goes
                // straight there and a refusal can be shown to the agent now
                // rather than failing silently in a queue.
                var result = await api.ClassifyAsync(communicationId, request.Classification);

                if (!result.IsOk)
                {
                    Message = Localizer[result.ErrorCode switch
                    {
                        "edit_window_closed" => "classification.tooOld",
                        "not_your_call" => "classification.notYours",
                        _ => "classification.saveFailed",
                    }];

                    return;
                }
            }
            else
            {
                // A call in progress: queued behind the call, which the server
                // has not been told about yet.
                await reporter.ClassifyAsync(request);
            }

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
