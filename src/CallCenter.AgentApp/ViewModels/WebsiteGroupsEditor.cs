using System.Collections.ObjectModel;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// The window where an agent makes their groups of tabs (A-88): a name, and
/// up to four tabs ticked. Nothing changes until Save.
/// </summary>
public sealed partial class WebsiteGroupsEditor : ObservableObject
{
    private readonly WebsitesViewModel _screen;

    public WebsiteGroupsEditor(WebsitesViewModel screen)
    {
        _screen = screen;
        Localizer = screen.Localizer;

        foreach (var group in screen.Groups)
        {
            Groups.Add(NewDraft(group.Name, group.Tabs));
        }

        if (Groups.Count == 0)
        {
            AddGroup();
        }
    }

    public Localizer Localizer { get; }

    public ObservableCollection<GroupDraft> Groups { get; } = [];

    /// <summary>Why Save did nothing, as a label key. Null when it can save.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Message))]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _messageKey;

    public string? Message => MessageKey is null ? null : Localizer[MessageKey];

    public bool HasMessage => MessageKey is not null;

    /// <summary>Raised when the window should close.</summary>
    public event EventHandler? Done;

    [RelayCommand]
    private void AddGroup() =>
        Groups.Add(NewDraft($"{Localizer["websites.groupDefault"]} {Groups.Count + 1}", []));

    [RelayCommand]
    private void RemoveGroup(GroupDraft? group)
    {
        if (group is not null)
        {
            Groups.Remove(group);
        }
    }

    /// <summary>Keeps the groups that have a name and a tab. A named group with no tab is pointed out.</summary>
    [RelayCommand]
    private void Save()
    {
        if (Groups.Any(g => string.IsNullOrWhiteSpace(g.Name) && g.Chosen.Any()))
        {
            MessageKey = "websites.groupNeedsName";
            return;
        }

        _screen.SetGroups(Groups
            .Where(g => !string.IsNullOrWhiteSpace(g.Name) && g.Chosen.Any())
            .Select(g => new AgentSettingsStore.WebsiteGroup(g.Name.Trim(), [.. g.Chosen])));

        Done?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => Done?.Invoke(this, EventArgs.Empty);

    private GroupDraft NewDraft(string name, IReadOnlyList<Guid> chosen)
    {
        var draft = new GroupDraft(this) { Name = name };

        foreach (var tab in _screen.Tabs)
        {
            draft.Choices.Add(new TabChoice(draft, tab, chosen.Contains(tab.Id)));
        }

        return draft;
    }

    internal void Refused() => MessageKey = "websites.groupMax";

    internal void Cleared() => MessageKey = null;

    /// <summary>One group being edited.</summary>
    public sealed partial class GroupDraft(WebsiteGroupsEditor editor) : ObservableObject
    {
        public WebsiteGroupsEditor Editor { get; } = editor;

        [ObservableProperty]
        private string _name = string.Empty;

        public ObservableCollection<TabChoice> Choices { get; } = [];

        /// <summary>The ticked tabs, in the order they are listed.</summary>
        public IEnumerable<Guid> Chosen => Choices.Where(c => c.IsChosen).Select(c => c.Tab.Id);
    }

    /// <summary>One tab's tick in a group. A fifth is refused: four is what fits on the screen.</summary>
    public sealed partial class TabChoice : ObservableObject
    {
        private readonly GroupDraft _group;
        private bool _isChosen;

        public TabChoice(GroupDraft group, WebsiteTab tab, bool chosen)
        {
            _group = group;
            Tab = tab;
            _isChosen = chosen;
        }

        public WebsiteTab Tab { get; }

        public bool IsChosen
        {
            get => _isChosen;
            set
            {
                if (value && !_isChosen && _group.Chosen.Count() >= WebsitesViewModel.MaxGroupSize)
                {
                    _group.Editor.Refused();
                    OnPropertyChanged();
                    return;
                }

                _group.Editor.Cleared();
                SetProperty(ref _isChosen, value);
            }
        }
    }
}
