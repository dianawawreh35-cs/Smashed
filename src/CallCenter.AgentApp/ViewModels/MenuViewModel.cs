using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.Shared.Contracts.Menu;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// The menu, as the agent reads it mid-call (A-66).
/// </summary>
/// <remarks>
/// Read-only. The supervisor sets prices (S-59); there is no editor here.
///
/// Searches as the agent types, with the same pause and the same
/// newer-request-wins rule as the delivery lookup: a slow answer for a prefix
/// landing after the answer for the whole word would put the wrong price on
/// screen, which is the only mistake here that matters.
/// </remarks>
public partial class MenuViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(350);

    private readonly ApiClient _api;
    private readonly DispatcherTimer _typingTimer;

    private CancellationTokenSource? _inFlight;

    public MenuViewModel(ApiClient api, Localizer localizer, Dispatcher dispatcher)
    {
        _api = api;
        Localizer = localizer;

        _typingTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TypingPause,
        };

        _typingTimer.Tick += (_, _) =>
        {
            _typingTimer.Stop();
            _ = SearchAsync();
        };

        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));

        localizer.LanguageChanged += (_, _) => OnPropertyChanged(nameof(StatusMessage));
    }

    public Localizer Localizer { get; }

    public ObservableCollection<MenuRow> Items { get; } = [];

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusKey;

    public bool HasStatus => StatusKey is not null;

    public bool HasResults => Items.Count > 0;

    public string? StatusMessage => StatusKey is null ? null : Localizer[StatusKey];

    partial void OnQueryChanged(string value)
    {
        _typingTimer.Stop();
        _typingTimer.Start();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var previous = _inFlight;
        var current = new CancellationTokenSource();
        _inFlight = current;
        previous?.Cancel();
        previous?.Dispose();

        IsBusy = true;
        StatusKey = null;

        try
        {
            var result = await _api.SearchMenuAsync(Query, current.Token);

            if (current.Token.IsCancellationRequested)
            {
                return;
            }

            Items.Clear();

            if (!result.IsOk || result.Value is null)
            {
                StatusKey = "menu.offline";
                return;
            }

            foreach (var item in result.Value)
            {
                Items.Add(new MenuRow(item, _api, Localizer));
            }

            if (Items.Count == 0)
            {
                StatusKey = "menu.noMatches";
            }
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer search.
        }
        finally
        {
            if (ReferenceEquals(_inFlight, current))
            {
                IsBusy = false;
            }
        }
    }

    public void Dispose()
    {
        _typingTimer.Stop();
        _inFlight?.Cancel();
        _inFlight?.Dispose();
    }
}

/// <summary>
/// One menu item, ready for the screen.
/// </summary>
/// <remarks>
/// The price needs saying carefully. Four cases, and confusing any two of them
/// costs the restaurant money or embarrasses the agent:
/// an item with a meal price, a plain price, an add-on ("+2"), and an item the
/// menu does not price at all.
/// </remarks>
public partial class MenuRow : ObservableObject
{
    private readonly MenuItemDto _item;
    private readonly ApiClient _api;
    private readonly Localizer _localizer;

    public MenuRow(MenuItemDto item, ApiClient api, Localizer localizer)
    {
        _item = item;
        _api = api;
        _localizer = localizer;

        if (item.HasImage)
        {
            // Fetched per row as the list renders, rather than with the search:
            // the list is what the agent reads, and it should not wait on a
            // megabyte of photographs to appear.
            _ = LoadImageAsync();
        }
    }

    public string Name => _item.Name;

    public string CategoryName => _item.CategoryName;

    public string Description => _item.Description ?? string.Empty;

    public bool HasDescription => !string.IsNullOrWhiteSpace(_item.Description);

    /// <summary>
    /// What to read out. Never a bare number for an add-on, and never a blank
    /// for an item the menu does not price.
    /// </summary>
    public string PriceText
    {
        get
        {
            if (_item.Price is null)
            {
                return _localizer["menu.priceNotShown"];
            }

            if (_item.IsSurcharge)
            {
                return _item.Price == 0
                    ? _localizer["menu.free"]
                    : $"+{_item.Price:0.##}";
            }

            return $"{_item.Price:0.##}";
        }
    }

    /// <summary>The meal price, shown beside the sandwich price where there is one.</summary>
    public string MealPriceText => _item.MealPrice is { } meal
        ? $"{_localizer["menu.meal"]} {meal:0.##}"
        : string.Empty;

    public bool HasMealPrice => _item.MealPrice is not null;

    [ObservableProperty]
    private BitmapImage? _image;

    private async Task LoadImageAsync()
    {
        var result = await _api.GetMenuImageAsync(_item.Id);

        if (!result.IsOk || result.Value is null || result.Value.Length == 0)
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream(result.Value);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();

            // Frozen so it can be handed to the UI thread from here.
            bitmap.Freeze();

            Image = bitmap;
        }
        catch (Exception)
        {
            // A picture that will not decode is not worth interrupting an agent
            // over; the row shows without it.
        }
    }
}
