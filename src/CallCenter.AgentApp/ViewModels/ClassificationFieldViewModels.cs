using System.Collections.ObjectModel;
using System.Text.Json;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.Shared.Contracts.Classifications;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// One question on the classification form (A-40, S-40).
/// </summary>
/// <remarks>
/// The supervisor decides what the form asks, so the fields are built at
/// runtime from the definition the server sends rather than written into the
/// screen. Each kind is its own class so WPF can pick a template for it by
/// type, which keeps the drawing declarative — no switch statement deciding
/// what to render.
///
/// <b>Why a field knows whether to show itself.</b> "Order value" is asked for
/// an order and a cancellation, "complaint reason" only for a complaint. That
/// rule lives in the definition as <c>showWhenType</c>, and it is matched
/// against the type's <b>name</b>, never its label — a supervisor renaming
/// "Complaint" to "Issue" must not make a field disappear.
/// </remarks>
public abstract partial class ClassificationFieldViewModel : ObservableObject
{
    protected ClassificationFieldViewModel(JsonElement field, Localizer localizer)
    {
        Localizer = localizer;
        Key = field.GetProperty("key").GetString()!;

        // The built-in fields carry no label in the definition - there is no
        // point making a supervisor name "type" in two languages - so they fall
        // back to the app's own words rather than to the key, which read as
        // "type" and "branch" in lower case on the first screen an agent saw.
        LabelAr = ReadLabel(field, "ar") ?? BuiltInLabel(Key, localizer, "ar") ?? Key;
        LabelEn = ReadLabel(field, "en") ?? BuiltInLabel(Key, localizer, "en") ?? Key;

        Required = field.TryGetProperty("required", out var required)
                   && required.ValueKind == JsonValueKind.True;

        ShowWhenType = field.TryGetProperty("showWhenType", out var when)
                       && when.ValueKind == JsonValueKind.Array
            ? when.EnumerateArray()
                .Where(t => t.ValueKind == JsonValueKind.String)
                .Select(t => t.GetString()!)
                .ToList()
            : [];
    }

    protected Localizer Localizer { get; }

    public string Key { get; }

    public string LabelAr { get; }

    public string LabelEn { get; }

    /// <summary>What the agent reads, in the language they are working in.</summary>
    public string Label => Localizer.IsArabic ? LabelAr : LabelEn;

    public bool Required { get; }

    /// <summary>
    /// The type names this field is asked for. Empty means always.
    /// </summary>
    public IReadOnlyList<string> ShowWhenType { get; }

    /// <summary>
    /// Whether this field applies to the type currently chosen.
    /// </summary>
    /// <remarks>
    /// Named <c>AppliesNow</c> and not <c>IsVisible</c> on purpose. Every WPF
    /// element already has an <c>IsVisible</c>, so an item container binding its
    /// Visibility to "IsVisible" bound to <b>its own</b> — a loop that showed
    /// the complaint field when nothing was selected and hid the notes field
    /// that should always be there.
    /// </remarks>
    [ObservableProperty]
    private bool _appliesNow = true;

    /// <summary>What the agent entered, or null when they entered nothing.</summary>
    public abstract object? Value { get; }

    /// <summary>Whether a required field has been answered.</summary>
    public virtual bool IsSatisfied => !Required || Value is not null;

    public void Retranslate() => OnPropertyChanged(nameof(Label));

    /// <summary>The app's own word for a field the system was built around.</summary>
    private static string? BuiltInLabel(string key, Localizer localizer, string language)
    {
        var lookup = key switch
        {
            "type" => "classification.fieldType",
            "branch" => "classification.fieldBranch",
            "order_value" => "classification.fieldOrderValue",
            "notes" => "classification.fieldNotes",
            "follow_up" => "classification.fieldFollowUp",
            _ => null,
        };

        return lookup is null ? null : localizer.InLanguage(lookup, language);
    }

    private static string? ReadLabel(JsonElement field, string language) =>
        field.TryGetProperty("label", out var label)
        && label.ValueKind == JsonValueKind.Object
        && label.TryGetProperty(language, out var text)
            ? text.GetString()
            : null;
}

/// <summary>The list of types (A-40). Always present; the form is refused without it.</summary>
public partial class TypeFieldViewModel : ClassificationFieldViewModel
{
    public TypeFieldViewModel(
        JsonElement field, Localizer localizer, IReadOnlyList<ClassificationTypeDto> types)
        : base(field, localizer)
    {
        // Hidden types are left out: they are what a supervisor retires a type
        // with, and offering one would put it on a new call.
        Types = new ObservableCollection<TypeOption>(
            types.Where(t => t.IsActive).Select(t => new TypeOption(t, localizer)));
    }

    public ObservableCollection<TypeOption> Types { get; }

    [ObservableProperty]
    private TypeOption? _selected;

    public override object? Value => Selected?.Type.Id;

    /// <summary>The type's stable name, which the show-when rules are matched on.</summary>
    public string? SelectedName => Selected?.Type.Name;
}

/// <summary>
/// One type, with the label for the language the agent is working in.
/// </summary>
/// <remarks>
/// A wrapper rather than binding straight to the DTO, because the DTO carries
/// both languages and the list has to show one. Keeping the DTO on it means the
/// id and the stable name are still to hand when the form is sent.
/// </remarks>
public class TypeOption(ClassificationTypeDto type, Localizer localizer)
{
    public ClassificationTypeDto Type { get; } = type;

    public string Label => localizer.IsArabic ? Type.LabelAr : Type.LabelEn;

    public override string ToString() => Label;
}

/// <summary>Which branch the call is about (A-40).</summary>
public partial class BranchFieldViewModel(
    JsonElement field, Localizer localizer, IReadOnlyList<FormBranchDto> branches)
    : ClassificationFieldViewModel(field, localizer)
{
    public IReadOnlyList<BranchOption> Branches { get; } =
        branches.Select(b => new BranchOption(b)).ToList();

    [ObservableProperty]
    private BranchOption? _selected;

    public override object? Value => Selected?.Branch.Id;
}

/// <summary>
/// One branch, as the list shows it.
/// </summary>
/// <remarks>
/// A wrapper rather than the DTO itself, and the reason is worth keeping: the
/// DTO is a <c>record</c>, and a record prints itself as all of its contents.
/// Bound directly, the closed box read
/// <c>FormBranchDto { Id = 8f3c…, Name = ايكون }</c> — which an agent
/// mid-call reported, reasonably, as gibberish. The type and the
/// supervisor-defined lists never showed it because they already went through
/// wrappers that print their label; the branch was the one that did not.
/// </remarks>
public class BranchOption(FormBranchDto branch)
{
    public FormBranchDto Branch { get; } = branch;

    public string Name => Branch.Name;

    public override string ToString() => Name;
}

/// <summary>A single line of text.</summary>
public partial class TextFieldViewModel(JsonElement field, Localizer localizer)
    : ClassificationFieldViewModel(field, localizer)
{
    [ObservableProperty]
    private string _text = string.Empty;

    public override object? Value => string.IsNullOrWhiteSpace(Text) ? null : Text.Trim();
}

/// <summary>Several lines — notes, mostly.</summary>
public partial class TextAreaFieldViewModel(JsonElement field, Localizer localizer)
    : ClassificationFieldViewModel(field, localizer)
{
    [ObservableProperty]
    private string _text = string.Empty;

    public override object? Value => string.IsNullOrWhiteSpace(Text) ? null : Text.Trim();
}

/// <summary>
/// A number — the order value, mostly.
/// </summary>
/// <remarks>
/// Kept as text and parsed on the way out rather than bound to a decimal. A
/// number box that fights the agent as they type "12." mid-thought is worse than
/// one that accepts anything and says so when it cannot be read.
/// </remarks>
public partial class NumberFieldViewModel(JsonElement field, Localizer localizer)
    : ClassificationFieldViewModel(field, localizer)
{
    [ObservableProperty]
    private string _text = string.Empty;

    public override object? Value =>
        decimal.TryParse(Text, out var number) ? number : null;

    /// <summary>Something was typed that is not a number.</summary>
    public bool IsUnreadable => !string.IsNullOrWhiteSpace(Text) && Value is null;

    public override bool IsSatisfied => !IsUnreadable && base.IsSatisfied;
}

/// <summary>A yes/no — follow-up required, mostly.</summary>
public partial class CheckboxFieldViewModel(JsonElement field, Localizer localizer)
    : ClassificationFieldViewModel(field, localizer)
{
    [ObservableProperty]
    private bool _isChecked;

    // Always answered: an unticked box is "no", not "unanswered", so a required
    // checkbox must not block saving.
    public override object? Value => IsChecked;
}

/// <summary>A list the supervisor wrote (S-40).</summary>
public partial class SelectFieldViewModel : ClassificationFieldViewModel
{
    public SelectFieldViewModel(JsonElement field, Localizer localizer)
        : base(field, localizer)
    {
        Options = field.TryGetProperty("options", out var options)
                  && options.ValueKind == JsonValueKind.Array
            ? options.EnumerateArray().Select(o => new SelectOption(o, localizer)).ToList()
            : [];
    }

    public IReadOnlyList<SelectOption> Options { get; }

    [ObservableProperty]
    private SelectOption? _selected;

    public override object? Value => Selected?.Value;
}

/// <summary>One choice in a supervisor-defined list.</summary>
public class SelectOption(JsonElement option, Localizer localizer)
{
    private readonly Localizer _localizer = localizer;

    public string Value { get; } = option.TryGetProperty("value", out var v)
        ? v.GetString() ?? string.Empty
        : string.Empty;

    private string LabelAr { get; } = ReadLabel(option, "ar");

    private string LabelEn { get; } = ReadLabel(option, "en");

    public string Label => _localizer.IsArabic
        ? (LabelAr.Length > 0 ? LabelAr : Value)
        : (LabelEn.Length > 0 ? LabelEn : Value);

    public override string ToString() => Label;

    private static string ReadLabel(JsonElement option, string language) =>
        option.TryGetProperty("label", out var label)
        && label.ValueKind == JsonValueKind.Object
        && label.TryGetProperty(language, out var text)
            ? text.GetString() ?? string.Empty
            : string.Empty;
}
