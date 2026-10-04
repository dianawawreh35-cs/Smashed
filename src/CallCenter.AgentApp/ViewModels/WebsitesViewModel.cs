using System.Collections.ObjectModel;
using System.ComponentModel;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.AgentApp.Services.Websites;
using CallCenter.Shared.Contracts.Websites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// The Websites screen (A-88): the tabs the supervisor listed, shown one at a
/// time, two side by side or four in quarters.
/// </summary>
/// <remarks>
/// <b>Four places, always.</b> The layout says how many are on screen (1, 2
/// or 4); each place holds a tab or none, and a tab is in one place at most.
/// Putting a tab where another place already has it swaps the two, so the
/// agent never loses one off the screen by choosing it twice.
///
/// <b>Made at each sign-in</b>, with the tabs the server lists for this
/// agent, and gone at sign-out with every browser in it (M-A06). The layout
/// and each tab's zoom are remembered for the agent on this laptop.
///
/// <b>Groups are the agent's own</b> (Dia, 4 Oct): up to four tabs under a
/// name, which open together, two side by side and three or four in quarters,
/// in the order the agent arranged them. Remembered with the layout, for that
/// agent on that laptop. <b>A group on screen is a tab of its own</b>: its
/// button is marked, and a tab button then replaces the whole group with that
/// one tab, rather than one quarter of it.
///
/// <b>Copies are the agent's own too</b> (Dia, 4 Oct 2026: two POS tabs at
/// once): a place's Duplicate button adds "POS 2", up to four of one site,
/// signed in as the original. Remembered with the layout, and closed by the
/// agent; the supervisor's own tabs cannot be closed.
///
/// <b>Silent during a call</b>, from the ring to the hang-up, whether or not
/// the agent muted a tab. The customer never hears the sites either way: only
/// the microphone goes down the line.
/// </remarks>
public sealed partial class WebsitesViewModel : ObservableObject, IDisposable, CartTab.ITarget
{
    public const int PlaceCount = 4;

    private readonly ApiClient _api;
    private readonly AgentSession _session;
    private readonly AgentSettingsStore _settings;
    private readonly WebsiteEngine _engine;
    private readonly CartTab _cartTab;
    private readonly CallViewModel _call;
    private readonly ILogger<WebsitesViewModel> _logger;

    public WebsitesViewModel(
        ApiClient api,
        AgentSession session,
        AgentSettingsStore settings,
        WebsiteEngine engine,
        CartTab cartTab,
        CallViewModel call,
        Localizer localizer,
        ILogger<WebsitesViewModel> logger)
    {
        _api = api;
        _session = session;
        _settings = settings;
        _engine = engine;
        _cartTab = cartTab;
        _call = call;
        _logger = logger;
        Localizer = localizer;

        Places = [.. Enumerable.Range(0, PlaceCount).Select(i => new WebsitePlace(this, i))];

        localizer.LanguageChanged += OnLanguageChanged;
        call.PropertyChanged += OnCallChanged;
        cartTab.Register(this);
    }

    public Localizer Localizer { get; }

    public ObservableCollection<WebsiteTab> Tabs { get; } = [];

    public IReadOnlyList<WebsitePlace> Places { get; }

    /// <summary>The agent's groups of tabs, as buttons beside the tabs.</summary>
    public ObservableCollection<WebsiteGroupItem> Groups { get; } = [];

    /// <summary>At most this many tabs in a group: what fits on the screen at once.</summary>
    public const int MaxGroupSize = PlaceCount;

    /// <summary>At most this many tabs of one site, the original among them: what fits on the screen at once.</summary>
    public const int MaxCopies = PlaceCount;

    /// <summary>Raised when what is shown where has changed, for the view to lay the browsers out.</summary>
    public event EventHandler? ArrangementChanged;

    /// <summary>1, 2 or 4.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOne))]
    [NotifyPropertyChangedFor(nameof(IsTwo))]
    [NotifyPropertyChangedFor(nameof(IsFour))]
    private int _shownPlaces = 1;

    public bool IsOne => ShownPlaces == 1;

    public bool IsTwo => ShownPlaces == 2;

    public bool IsFour => ShownPlaces == 4;

    /// <summary>The place a tab chosen from the tab bar goes into: the last one the agent used.</summary>
    [ObservableProperty]
    private int _activePlace;

    /// <summary>Why there are no tabs, as a label key. Null when there are.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Message))]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _messageKey = "websites.loading";

    public string? Message => MessageKey is null ? null : Localizer[MessageKey];

    public bool HasMessage => MessageKey is not null;

    private bool _loaded;

    /// <summary>
    /// Asks the server for the tabs and starts the ones that alert. Called
    /// once, when the signed-in screen is built.
    /// </summary>
    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        if (_session.User is not { } user)
        {
            return;
        }

        var result = await _api.GetMyWebsitesAsync();

        if (!result.IsOk || result.Value is null)
        {
            _loaded = false;
            MessageKey = "websites.offline";
            return;
        }

        if (await _engine.GetAsync() is null)
        {
            MessageKey = "websites.noEngine";
            return;
        }

        var layout = _settings.Current.WebsiteLayouts.GetValueOrDefault(user.Id.ToString());

        foreach (var listed in result.Value)
        {
            // A-88: a site on the agent's own login (the POS) takes the same
            // username and password as the Call Center, so the app types them
            // in, as it does a shared login. A site that refuses them is left
            // for the agent to log in to by hand.
            var site = listed.Login == WebsiteLogins.Own && !string.IsNullOrEmpty(_session.Password)
                ? listed with { Username = user.Login, Password = _session.Password }
                : listed;

            // The original, then the copies the agent left open.
            var copies = layout?.Copies?.GetValueOrDefault(site.Id.ToString()) ?? [];

            foreach (var number in copies.Where(n => n is > 1 and <= MaxCopies).Prepend(1).Distinct().Order())
            {
                var tab = new WebsiteTab(site, user.Id, _engine, Localizer, _logger, number);

                if (layout?.Zoom.TryGetValue(tab.Id.ToString(), out var zoom) is true)
                {
                    tab.Zoom = zoom;
                }

                tab.PropertyChanged += OnTabChanged;
                Tabs.Add(tab);
            }
        }

        MessageKey = Tabs.Count == 0 ? "websites.none" : null;

        RestoreLayout(layout);
        SetGroups(layout?.Groups ?? []);

        // A site that alerts has to be running to be heard.
        foreach (var tab in Tabs.Where(t => t.AlertsWithSound))
        {
            _ = tab.StartAsync();
        }

        ApplyCallSilence();
        Arrange();
    }

    /// <summary>
    /// The group whose tabs are exactly what is on screen, in the layout that
    /// fits them, or null. Worked out from the places, never stored, so it
    /// cannot disagree with what the agent sees.
    /// </summary>
    public WebsiteGroupItem? ShownGroup { get; private set; }

    /// <summary>
    /// The tab-bar button: the tab goes into the active place. With a group
    /// on screen it takes the whole group's place instead, alone (A-88).
    /// </summary>
    [RelayCommand]
    private void Show(WebsiteTab? tab)
    {
        if (tab is null)
        {
            return;
        }

        if (ShownGroup is not null)
        {
            ShownPlaces = 1;
            Put(0, tab);
        }
        else
        {
            Put(ActivePlace, tab);
        }
    }

    /// <summary>
    /// A group's button: its tabs on screen together, in the layout that fits
    /// them, and nothing else beside them.
    /// </summary>
    [RelayCommand]
    private void ShowGroup(WebsiteGroupItem? group)
    {
        if (group is null)
        {
            return;
        }

        var tabs = group.Tabs.Select(id => Tabs.FirstOrDefault(t => t.Id == id)).OfType<WebsiteTab>()
            .Take(MaxGroupSize).ToList();

        if (tabs.Count == 0)
        {
            return;
        }

        ShownPlaces = PlacesFor(tabs.Count);

        for (var i = 0; i < PlaceCount; i++)
        {
            Places[i].SetTab(i < tabs.Count ? tabs[i] : null);
        }

        ActivePlace = 0;
        Save();
        Arrange();
    }

    /// <summary>
    /// Replaces the agent's groups with <paramref name="groups"/>, from the
    /// groups window. Tabs the supervisor has since removed drop out, and a
    /// group left with none goes.
    /// </summary>
    public void SetGroups(IEnumerable<AgentSettingsStore.WebsiteGroup> groups)
    {
        Groups.Clear();

        foreach (var group in groups)
        {
            var tabs = group.Tabs.Where(id => Tabs.Any(t => t.Id == id)).Distinct().Take(MaxGroupSize).ToArray();

            if (tabs.Length > 0 && !string.IsNullOrWhiteSpace(group.Name))
            {
                Groups.Add(new WebsiteGroupItem(group.Name.Trim(), tabs));
            }
        }

        MarkShownGroup();
        Save();
    }

    /// <summary>The layout a group of <paramref name="count"/> tabs opens in.</summary>
    private static int PlacesFor(int count) => count switch { 1 => 1, 2 => 2, _ => 4 };

    /// <summary>Finds the group on screen, if any, and marks its button.</summary>
    private void MarkShownGroup()
    {
        var onScreen = Places.Take(ShownPlaces).Select(p => p.Tab?.Id).OfType<Guid>().ToHashSet();
        ShownGroup = Groups.FirstOrDefault(g => PlacesFor(g.Tabs.Count) == ShownPlaces && onScreen.SetEquals(g.Tabs));

        foreach (var group in Groups)
        {
            group.IsShown = group == ShownGroup;
        }
    }

    /// <summary>Raised by the Groups button; the view opens the window.</summary>
    public event EventHandler? GroupsRequested;

    [RelayCommand]
    private void EditGroups() => GroupsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void SetLayout(string? places)
    {
        if (int.TryParse(places, out var count) && count is 1 or 2 or 4)
        {
            ShownPlaces = count;

            // Fill the places that came on screen with tabs not on screen yet.
            foreach (var place in Places.Take(count).Where(p => p.Tab is null))
            {
                place.SetTab(Tabs.FirstOrDefault(t => !IsOnScreen(t)));
            }

            if (ActivePlace >= count)
            {
                ActivePlace = 0;
            }

            Save();
            Arrange();
        }
    }

    /// <summary>
    /// Puts <paramref name="tab"/> in place <paramref name="index"/>. A tab
    /// already in another place swaps with what was here.
    /// </summary>
    public void Put(int index, WebsiteTab? tab)
    {
        var place = Places[index];

        if (tab is not null && Places.FirstOrDefault(p => p != place && p.Tab == tab) is { } other)
        {
            other.SetTab(place.Tab);
        }

        place.SetTab(tab);
        ActivePlace = index;
        Save();
        Arrange();
    }

    /// <summary>
    /// The place's Duplicate button: another tab on the same site, "POS 2",
    /// on the page the original is showing and signed in as it is (the same
    /// profile). It goes in the original's place, as a browser shows the
    /// copy it has just made; the original keeps its button.
    /// </summary>
    public void Duplicate(int index)
    {
        if (Places[index].Tab is not { } from || _session.User is not { } user)
        {
            return;
        }

        var taken = Tabs.Where(t => t.Site.Id == from.Site.Id).Select(t => t.Number).ToHashSet();
        var number = Enumerable.Range(2, MaxCopies - 1).FirstOrDefault(n => !taken.Contains(n));

        if (number == 0)
        {
            from.Say("websites.copyMax");
            return;
        }

        var copy = new WebsiteTab(from.Site, user.Id, _engine, Localizer, _logger, number, from.CurrentUrl)
        {
            Zoom = from.Zoom,
        };
        copy.SetCallSilence(_call.State.IsActive);
        copy.PropertyChanged += OnTabChanged;

        // Beside the original, in number order: POS, POS 2, POS 3.
        var before = Tabs.Last(t => t.Site.Id == from.Site.Id && t.Number < number);
        Tabs.Insert(Tabs.IndexOf(before) + 1, copy);

        _logger.LogInformation("Website tab {Name}: copy {Number} opened", from.Site.NameEn, number);
        Put(index, copy);
    }

    /// <summary>
    /// A copy's Close button. Where it was shown, the original comes back if
    /// it is not on screen already, or another tab that is not; and it leaves
    /// the groups it was in.
    /// </summary>
    public void CloseCopy(WebsiteTab? tab)
    {
        if (tab is not { IsCopy: true } || !Tabs.Contains(tab))
        {
            return;
        }

        var original = Tabs.First(t => t.Site.Id == tab.Site.Id && !t.IsCopy);

        foreach (var place in Places.Where(p => p.Tab == tab))
        {
            place.SetTab(Places.Any(p => p.Tab == original) ? null : original);
        }

        tab.PropertyChanged -= OnTabChanged;
        Tabs.Remove(tab);

        foreach (var place in Places.Take(ShownPlaces).Where(p => p.Tab is null))
        {
            place.SetTab(Tabs.FirstOrDefault(t => !Places.Any(p => p.Tab == t)));
        }

        // Saves too, with the copy gone from the groups and the list.
        SetGroups([.. Groups.Select(g => new AgentSettingsStore.WebsiteGroup(g.Name, [.. g.Tabs]))]);
        Arrange();

        _logger.LogInformation("Website tab {Name}: copy {Number} closed", tab.Site.NameEn, tab.Number);
        tab.Dispose();
    }

    /// <summary>The place a tab is shown in, or -1.</summary>
    public int PlaceOf(WebsiteTab tab)
    {
        for (var i = 0; i < ShownPlaces; i++)
        {
            if (Places[i].Tab == tab)
            {
                return i;
            }
        }

        return -1;
    }

    private bool IsOnScreen(WebsiteTab tab) => PlaceOf(tab) >= 0;

    /// <summary>A-85 inside the app: the caller's cart in the tab that takes it.</summary>
    public bool TryOpen(string localNumber)
    {
        if (Tabs.FirstOrDefault(t => t.OpensCart) is not { } tab)
        {
            return false;
        }

        var url = tab.Site.CartUrl!.Replace(
            "{number}", Uri.EscapeDataString(localNumber), StringComparison.OrdinalIgnoreCase);

        tab.Navigate(url);

        // Ready where the agent will look for it: on screen, if it is not
        // already, as if its tab button were pressed. The agent stays on the
        // pop-up meanwhile.
        if (!IsOnScreen(tab))
        {
            Show(tab);
        }

        _logger.LogInformation("Opened the caller's cart in the {Name} tab", tab.Site.NameEn);
        return true;
    }

    /// <summary>Lays the browsers out, and starts the ones that have come on screen.</summary>
    private void Arrange()
    {
        foreach (var tab in Tabs)
        {
            var shown = IsOnScreen(tab);
            tab.SetShown(shown);

            if (shown)
            {
                _ = tab.StartAsync();
            }
        }

        for (var i = 0; i < PlaceCount; i++)
        {
            Places[i].IsShown = i < ShownPlaces;
            Places[i].IsActive = i == ActivePlace && ShownPlaces > 1;
        }

        MarkShownGroup();
        ArrangementChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RestoreLayout(AgentSettingsStore.WebsiteLayout? layout)
    {
        ShownPlaces = layout?.Places is 1 or 2 or 4 ? layout.Places : 1;

        for (var i = 0; i < PlaceCount; i++)
        {
            var id = layout?.Tabs.ElementAtOrDefault(i);
            var tab = Tabs.FirstOrDefault(t => t.Id == id);

            // A tab the supervisor removed, or one already placed, leaves the place empty.
            Places[i].SetTab(tab is not null && Places.Take(i).All(p => p.Tab != tab) ? tab : null);
        }

        foreach (var place in Places.Take(ShownPlaces).Where(p => p.Tab is null))
        {
            place.SetTab(Tabs.FirstOrDefault(t => !Places.Any(p => p.Tab == t)));
        }
    }

    private void Save()
    {
        if (_session.User is not { } user || Tabs.Count == 0)
        {
            return;
        }

        var layout = new AgentSettingsStore.WebsiteLayout(
            ShownPlaces,
            [.. Places.Select(p => p.Tab?.Id)],
            Tabs.ToDictionary(t => t.Id.ToString(), t => t.Zoom),
            [.. Groups.Select(g => new AgentSettingsStore.WebsiteGroup(g.Name, [.. g.Tabs]))],
            Tabs.Where(t => t.IsCopy).GroupBy(t => t.Site.Id)
                .ToDictionary(g => g.Key.ToString(), g => g.Select(t => t.Number).ToArray()));

        _settings.Update(s => s with
        {
            WebsiteLayouts = new Dictionary<string, AgentSettingsStore.WebsiteLayout>(s.WebsiteLayouts)
            {
                [user.Id.ToString()] = layout,
            },
        });
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WebsiteTab.Zoom))
        {
            Save();
        }
    }

    private void OnCallChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CallViewModel.State))
        {
            ApplyCallSilence();
        }
    }

    /// <summary>From the first ring to the hang-up.</summary>
    private void ApplyCallSilence()
    {
        var silent = _call.State.IsActive;

        foreach (var tab in Tabs)
        {
            tab.SetCallSilence(silent);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var tab in Tabs)
        {
            tab.RefreshLabels();
        }

        OnPropertyChanged(nameof(Message));
    }

    public void Dispose()
    {
        _cartTab.Unregister(this);
        Localizer.LanguageChanged -= OnLanguageChanged;
        _call.PropertyChanged -= OnCallChanged;

        foreach (var tab in Tabs)
        {
            tab.PropertyChanged -= OnTabChanged;
            tab.Dispose();
        }
    }
}

/// <summary>One of the agent's groups, as its button shows it: the name and how many tabs.</summary>
public sealed partial class WebsiteGroupItem(string name, IReadOnlyList<Guid> tabs) : ObservableObject
{
    public string Name { get; } = name;

    /// <summary>In the order the agent arranged them: the first place, then the next.</summary>
    public IReadOnlyList<Guid> Tabs { get; } = tabs;

    public string Label => $"{Name} ▸ {Tabs.Count}";

    /// <summary>Its tabs are what is on screen: the button is marked, as the tab being shown.</summary>
    [ObservableProperty]
    private bool _isShown;
}

/// <summary>One of the four places on the Websites screen, and the tab in it.</summary>
public sealed partial class WebsitePlace(WebsitesViewModel screen, int index) : ObservableObject
{
    public int Index { get; } = index;

    public WebsitesViewModel Screen { get; } = screen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTab))]
    private WebsiteTab? _tab;

    [ObservableProperty]
    private bool _isShown;

    /// <summary>Where a tab chosen from the tab bar goes. Marked only when there is more than one place.</summary>
    [ObservableProperty]
    private bool _isActive;

    public bool HasTab => Tab is not null;

    /// <summary>
    /// Bound to the place's list of tabs: choosing one there puts it here.
    /// A null is ignored: the list writes one back when its items change,
    /// and the agent has no way to choose "none".
    /// </summary>
    public WebsiteTab? Chosen
    {
        get => Tab;
        set
        {
            if (value is not null && value != Tab)
            {
                Screen.Put(Index, value);
            }
        }
    }

    /// <summary>Changes the tab without the swapping and saving of <see cref="WebsitesViewModel.Put"/>.</summary>
    internal void SetTab(WebsiteTab? tab)
    {
        Tab = tab;
        OnPropertyChanged(nameof(Chosen));
    }

    [RelayCommand]
    private void Duplicate() => Screen.Duplicate(Index);

    [RelayCommand]
    private void CloseCopy() => Screen.CloseCopy(Tab);

    /// <summary>A click anywhere in the place's bar makes it the one the tab bar fills.</summary>
    [RelayCommand]
    private void Activate()
    {
        if (Screen.ActivePlace != Index)
        {
            Screen.ActivePlace = Index;

            foreach (var place in Screen.Places)
            {
                place.IsActive = place.Index == Index && Screen.ShownPlaces > 1;
            }
        }
    }
}
