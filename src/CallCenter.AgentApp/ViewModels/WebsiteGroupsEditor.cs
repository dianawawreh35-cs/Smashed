using System.Collections.ObjectModel;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// The window where an agent makes their groups of tabs (A-88): a name, up
/// to four tabs ticked, and the order they go on screen in. Nothing changes
/// until Save.
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

        // The saved order, not the order the tabs are listed in.
        foreach (var id in chosen)
        {
            if (draft.Choices.FirstOrDefault(c => c.Tab.Id == id && c.IsChosen) is { } choice)
            {
                draft.Order.Add(choice);
            }
        }

        draft.Renumber();
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

        /// <summary>
        /// The ticked tabs in the order they go on screen: 1 and 2 on the top
        /// row, 3 and 4 below, in reading order (so 1 is on the right in Arabic).
        /// A new tick goes last.
        /// </summary>
        public ObservableCollection<TabChoice> Order { get; } = [];

        /// <summary>The ticked tabs, in their order on screen.</summary>
        public IEnumerable<Guid> Chosen => Order.Select(c => c.Tab.Id);

        /// <summary>An order is only worth showing for two tabs or more.</summary>
        public bool HasOrder => Order.Count > 1;

        internal void Move(TabChoice choice, int by)
        {
            var from = Order.IndexOf(choice);
            var to = from + by;

            if (from >= 0 && to >= 0 && to < Order.Count)
            {
                Order.Move(from, to);
                Renumber();
            }
        }

        internal void Renumber()
        {
            for (var i = 0; i < Order.Count; i++)
            {
                Order[i].SetPosition(i + 1, Order.Count);
            }

            OnPropertyChanged(nameof(HasOrder));
        }
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

        public Localizer Localizer => _group.Editor.Localizer;

        /// <summary>Where it goes on screen, from 1, while it is ticked.</summary>
        [ObservableProperty]
        private int _position;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
        private bool _isFirst;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
        private bool _isLast;

        internal void SetPosition(int position, int count)
        {
            Position = position;
            IsFirst = position == 1;
            IsLast = position == count;
        }

        [RelayCommand(CanExecute = nameof(CanMoveUp))]
        private void MoveUp() => _group.Move(this, -1);

        private bool CanMoveUp() => !IsFirst;

        [RelayCommand(CanExecute = nameof(CanMoveDown))]
        private void MoveDown() => _group.Move(this, 1);

        private bool CanMoveDown() => !IsLast;

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

                if (SetProperty(ref _isChosen, value))
                {
                    if (value)
                    {
                        _group.Order.Add(this);
                    }
                    else
                    {
                        _group.Order.Remove(this);
                    }

                    _group.Renumber();
                }
            }
        }
    }
}
