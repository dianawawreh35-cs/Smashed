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
    private readonly MenuImageCache _images;
    private readonly DispatcherTimer _typingTimer;

    private CancellationTokenSource? _inFlight;

    public MenuViewModel(
        ApiClient api, MenuImageCache images, Localizer localizer, Dispatcher dispatcher)
    {
        _api = api;
        _images = images;
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
                Items.Add(new MenuRow(item, _images, Localizer));
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
    private readonly MenuImageCache _images;
    private readonly Localizer _localizer;

    public MenuRow(MenuItemDto item, MenuImageCache images, Localizer localizer)
    {
        _item = item;
        _images = images;
        _localizer = localizer;

        if (item.HasImage)
        {
            // Fetched per row as the list renders, rather than with the search:
            // the list is what the agent reads, and it should not wait on a
            // megabyte of photographs to appear. The cache makes this cheap -
            // a picture already fetched comes back without a request, and no
            // more than four are ever in flight.
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

    /// <summary>
    /// The picture was promised by the row but did not arrive.
    /// </summary>
    /// <remarks>
    /// Kept apart from "this item has no photograph" because the two used to
    /// draw the same empty box, and that is exactly how 39 failed fetches a
    /// search stayed invisible. Five of the 44 items genuinely have none - the
    /// printed menu does not photograph the add-ons.
    /// </remarks>
    [ObservableProperty]
    private bool _imageFailed;

    /// <summary>
    /// What to write in the empty picture box: nothing while one is still on
    /// its way, "no picture" for an item that never had one, and "could not be
    /// loaded" when one was promised and did not arrive.
    /// </summary>
    /// <remarks>
    /// The last two used to look identical, and that is the whole reason a
    /// screenful of failed fetches passed for a menu of add-ons.
    /// </remarks>
    public string PictureStatusText =>
        Image is not null ? string.Empty
        : ImageFailed ? _localizer["menu.pictureFailed"]
        : !_item.HasImage ? _localizer["menu.noPicture"]
        : string.Empty;

    partial void OnImageChanged(BitmapImage? value) =>
        OnPropertyChanged(nameof(PictureStatusText));

    partial void OnImageFailedChanged(bool value) =>
        OnPropertyChanged(nameof(PictureStatusText));

    private async Task LoadImageAsync()
    {
        var bitmap = await _images.GetAsync(_item.Id);

        if (bitmap is null)
        {
            ImageFailed = true;
            return;
        }

        Image = bitmap;
    }
}
