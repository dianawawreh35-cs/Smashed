using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CallCenter.Server.Features.Menu;

/// <summary>Where menu photographs live on disk.</summary>
public class MenuImageOptions
{
    public const string SectionName = "MenuImages";

    /// <summary>
    /// The folder. Deployed as <c>/data/menu-images</c>, beside the recordings,
    /// so the nightly rsync in runbook step 9 covers both.
    /// </summary>
    [Required]
    public string Path { get; set; } = "data/menu-images";
}

/// <summary>
/// The menu photographs, as files (A-66, S-59).
/// </summary>
/// <remarks>
/// <b>Files rather than rows.</b> The first version held them in the database
/// as <c>bytea</c>, on the reasoning that the backup already covered the
/// database. That was wrong twice over. The backup script already rsyncs a data
/// folder — recordings — so files were always covered; and more importantly
/// <b>rsync is incremental while <c>pg_dump</c> is not</b>. Menu photographs
/// never change, so on disk they are copied once. In the database they were
/// re-dumped and re-copied every night, for ever.
///
/// <b>What files cost.</b> A row and its file cannot be written atomically, so
/// two things can go wrong, and both are deliberately benign: a file left behind
/// after its item is deleted wastes a few kilobytes and nothing else, and a row
/// whose file is missing shows without a picture and answers 404 for it. Neither
/// loses data or confuses an agent, which is what makes the trade acceptable.
///
/// The file is named after the item's id, never after anything typed — there is
/// no path to traverse.
/// </remarks>
public class MenuImageStore(IOptions<MenuImageOptions> options, ILogger<MenuImageStore> logger)
{
    private readonly string _root = options.Value.Path;

    /// <summary>The media types a picture may be, and the extension each gets.</summary>
    private static readonly Dictionary<string, string> Extensions = new()
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp",
    };

    /// <summary>The media type to serve a stored file as, from its extension.</summary>
    private static readonly Dictionary<string, string> ContentTypes = new()
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
    };

    public static bool IsAllowed(string? contentType) =>
        contentType is not null && Extensions.ContainsKey(contentType);

    /// <summary>
    /// Writes an item's picture, replacing whatever it had. Returns the file
    /// name to store on the row, or null when the bytes could not be written.
    /// </summary>
    /// <remarks>
    /// Any previous picture is removed first, because a change of format leaves
    /// the old extension behind and both would then exist.
    /// </remarks>
    public async Task<string?> SaveAsync(
        Guid itemId, byte[] bytes, string contentType, CancellationToken ct = default)
    {
        if (!Extensions.TryGetValue(contentType, out var extension))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_root);

            Delete(itemId);

            var name = itemId + extension;
            await File.WriteAllBytesAsync(System.IO.Path.Combine(_root, name), bytes, ct);

            return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "The picture for menu item {ItemId} could not be written to {Root}",
                itemId, _root);
            return null;
        }
    }

    /// <summary>
    /// Removes every picture belonging to an item, whatever its format.
    /// </summary>
    /// <remarks>
    /// Never throws. A file that will not delete leaves a few kilobytes behind
    /// and must not fail the request that removed the item.
    /// </remarks>
    public void Delete(Guid itemId)
    {
        try
        {
            if (!Directory.Exists(_root))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_root, itemId + ".*"))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "A picture for menu item {ItemId} could not be removed", itemId);
        }
    }

    /// <summary>
    /// Opens a stored picture, or returns null when the file is not there.
    /// </summary>
    /// <remarks>
    /// A missing file is normal rather than exceptional: the row and the file
    /// are written separately, and a restore that brought the database back
    /// without the folder would hit exactly this. The caller answers 404 and the
    /// screen shows the item without a picture.
    /// </remarks>
    public (Stream Stream, string ContentType)? Open(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        // The name comes from our own column and is always "<guid><ext>", but
        // reading only the file name part costs nothing and removes the question.
        var safe = System.IO.Path.GetFileName(fileName);
        var path = System.IO.Path.Combine(_root, safe);

        if (!File.Exists(path))
        {
            logger.LogWarning("Menu picture {File} is recorded but missing from {Root}", safe, _root);
            return null;
        }

        var extension = System.IO.Path.GetExtension(safe).ToLowerInvariant();

        if (!ContentTypes.TryGetValue(extension, out var contentType))
        {
            return null;
        }

        try
        {
            return (File.OpenRead(path), contentType);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Menu picture {File} could not be opened", safe);
            return null;
        }
    }
}
