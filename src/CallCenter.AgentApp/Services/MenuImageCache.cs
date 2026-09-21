using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services;

/// <summary>
/// The menu photographs, fetched once and not again (A-66).
/// </summary>
/// <remarks>
/// <b>Why this exists.</b> Each row used to fetch its own picture the moment it
/// was built, with nothing holding the results and nothing limiting how many
/// went at once. Searching the menu therefore fired <b>39 simultaneous
/// requests</b> — and did it again on every keystroke, because typing rebuilds
/// the rows. On the development machine most of them timed out after ten
/// seconds and the pictures silently never appeared; worse, they starved the
/// rest of the app, so the delivery search and the call log timed out too while
/// an agent was only browsing the menu.
///
/// Two rules fix it:
///
/// <list type="bullet">
/// <item><b>Fetched once per picture, for the life of the app.</b> Menu
/// photographs do not change while an agent is signed in, and a supervisor's
/// edit reaching an agent's screen a shift late is not worth a refetch on every
/// search. 1.2 MB of decoded bitmaps is nothing beside what WPF already
/// holds.</item>
/// <item><b>At most four at a time.</b> The rest wait rather than pile up. The
/// agent sees the first pictures just as fast and the app stays responsive,
/// which over a VPN to the real server is the difference between a menu that
/// loads and one that does not.</item>
/// </list>
///
/// A picture that fails is remembered as failed, so a broken one is not retried
/// 39 times a search. It is remembered as <see langword="null"/>, which the row
/// shows as "could not be loaded" rather than as an item with no photograph —
/// telling those two apart is what would have made this visible on day one.
/// </remarks>
public class MenuImageCache(ApiClient api, ILogger<MenuImageCache> logger)
{
    /// <summary>
    /// How many pictures may be in flight together.
    /// </summary>
    /// <remarks>
    /// Four rather than one: the first screenful arrives quickly. Four rather
    /// than all of them: the connection pool is shared with everything else the
    /// app does, including the block-list refresh that decides whether a barred
    /// caller gets through.
    /// </remarks>
    private const int MaxConcurrent = 4;

    private readonly SemaphoreSlim _gate = new(MaxConcurrent, MaxConcurrent);

    // A started fetch is cached, not a finished one, so two rows asking for the
    // same picture at the same time share one request instead of making two.
    private readonly Dictionary<Guid, Task<BitmapImage?>> _images = [];

    /// <summary>
    /// The picture for an item, or <see langword="null"/> when it could not be
    /// fetched or decoded.
    /// </summary>
    public Task<BitmapImage?> GetAsync(Guid id)
    {
        lock (_images)
        {
            if (_images.TryGetValue(id, out var existing))
            {
                return existing;
            }

            var fetch = FetchAsync(id);
            _images[id] = fetch;
            return fetch;
        }
    }

    private async Task<BitmapImage?> FetchAsync(Guid id)
    {
        await _gate.WaitAsync();

        try
        {
            var result = await api.GetMenuImageAsync(id);

            if (!result.IsOk || result.Value is null || result.Value.Length == 0)
            {
                // Logged, unlike before. A picture that never arrives used to
                // leave no trace anywhere, which is why 39 failures a search
                // went unnoticed.
                logger.LogWarning("The picture for menu item {ItemId} could not be fetched", id);
                return null;
            }

            using var stream = new MemoryStream(result.Value);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();

            // Frozen so it can be handed to the UI thread from here, and shared
            // by every row that asks for it.
            bitmap.Freeze();

            return bitmap;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The picture for menu item {ItemId} could not be decoded", id);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
