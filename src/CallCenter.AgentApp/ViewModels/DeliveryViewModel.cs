using System.Collections.ObjectModel;
using System.Windows.Threading;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.Shared.Contracts.Delivery;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// Which branch delivers to a place, and what it costs (A-65).
/// </summary>
/// <remarks>
/// Read-only. The agent quotes the price; only the supervisor sets it (S-58),
/// so there is no editor here at all.
///
/// The agent is usually mid-call with a customer waiting, so this searches as
/// they type rather than making them press anything. A pause of a few hundred
/// milliseconds, so a whole place name costs one request instead of one per
/// letter.
/// </remarks>
public partial class DeliveryViewModel : ObservableObject, IDisposable
{
    /// <summary>How long typing has to stop before the search is sent.</summary>
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(350);

    private readonly ApiClient _api;
    private readonly DispatcherTimer _typingTimer;

    private CancellationTokenSource? _inFlight;

    public DeliveryViewModel(ApiClient api, Localizer localizer, Dispatcher dispatcher)
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

        Areas.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));

        localizer.LanguageChanged += (_, _) => OnPropertyChanged(nameof(StatusMessage));
    }

    public Localizer Localizer { get; }

    public ObservableCollection<DeliveryAreaDto> Areas { get; } = [];

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusKey;

    public bool HasStatus => StatusKey is not null;

    public bool HasResults => Areas.Count > 0;

    public string? StatusMessage => StatusKey is null ? null : Localizer[StatusKey];

    partial void OnQueryChanged(string value)
    {
        _typingTimer.Stop();
        _typingTimer.Start();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        // A newer search replaces an older one, so a slow answer for a prefix
        // cannot land after the answer for the whole word and show the wrong
        // price — which is the one mistake that matters here.
        var previous = _inFlight;
        var current = new CancellationTokenSource();
        _inFlight = current;
        previous?.Cancel();
        previous?.Dispose();

        IsBusy = true;
        StatusKey = null;

        try
        {
            var result = await _api.SearchDeliveryAreasAsync(Query, current.Token);

            if (current.Token.IsCancellationRequested)
            {
                return;
            }

            Areas.Clear();

            if (!result.IsOk || result.Value is null)
            {
                StatusKey = "delivery.offline";
                return;
            }

            foreach (var area in result.Value)
            {
                Areas.Add(area);
            }

            if (Areas.Count == 0)
            {
                // Not an error: the place may simply not be on the list, and the
                // agent needs to know that rather than assume the app is broken.
                StatusKey = Query.Trim().Length == 0 ? null : "delivery.noMatches";
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
