using System.Globalization;
using System.Text.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared.Contracts.AgentLogs;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.AgentLogs;

/// <summary>
/// The Logs page's red line about today's errors (S-64), once a supervisor has
/// acknowledged it: which errors were seen, by whom, and when.
/// </summary>
/// <remarks>
/// <b>Counts per laptop, not a flag.</b> The page hides the line while no laptop
/// has more errors today than were acknowledged, so an error that arrives
/// afterwards brings it back, counted on its own. One acknowledgement for
/// everybody: the line is the team's, and one supervisor answering it is
/// enough.
///
/// Kept in <c>settings</c> as one JSON row, as the nicknames are
/// (<see cref="AgentLogNicknames"/>): no migration, and not on the Settings
/// page. The day is the browser's, as the page counts it, since the laptops
/// name their files by their own clock.
/// </remarks>
public class AgentLogAcknowledgements(CallCenterDbContext db, TimeProvider clock)
{
    public const string Key = "agent_logs.errors_acknowledged";

    /// <summary>More laptops than this in one acknowledgement is not the call centre's.</summary>
    public const int MaxLaptops = 200;

    private record Stored(string Date, Dictionary<string, int> Errors);

    /// <summary>The latest acknowledgement, or null when there has been none.</summary>
    public async Task<AgentLogAcknowledgementDto?> LatestAsync(CancellationToken ct)
    {
        var row = await db.Settings.AsNoTracking()
            .Where(s => s.Key == Key)
            .Select(s => new { s.Value, s.UpdatedAt, By = s.UpdatedByUser == null ? null : s.UpdatedByUser.DisplayName })
            .FirstOrDefaultAsync(ct);

        var stored = row is null ? null : Read(row.Value);

        return stored is null
            ? null
            : new AgentLogAcknowledgementDto(stored.Date, stored.Errors, row!.By, row.UpdatedAt);
    }

    /// <summary>Whether <paramref name="request"/> is one to keep; the reason when not.</summary>
    public static string? Problem(AcknowledgeAgentLogErrorsRequest request)
    {
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return "The date is not yyyy-MM-dd.";
        }

        if (request.Errors is null || request.Errors.Count > MaxLaptops)
        {
            return $"Give each laptop's errors, for at most {MaxLaptops} laptops.";
        }

        return request.Errors.Any(e => !AgentLogNames.IsLaptop(e.Key) || e.Value < 0)
            ? "A laptop name is not one the server keeps logs under, or a count is below zero."
            : null;
    }

    /// <summary>Keeps <paramref name="request"/> as the latest acknowledgement.</summary>
    public async Task<AgentLogAcknowledgementDto> AcknowledgeAsync(
        AcknowledgeAgentLogErrorsRequest request, Guid by, CancellationToken ct)
    {
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == Key, ct);

        if (row is null)
        {
            row = new Setting { Key = Key };
            db.Settings.Add(row);
        }

        row.Value = JsonSerializer.Serialize(new Stored(request.Date, new Dictionary<string, int>(request.Errors)));
        row.UpdatedAt = clock.GetUtcNow();
        row.UpdatedBy = by;
        await db.SaveChangesAsync(ct);

        return (await LatestAsync(ct))!;
    }

    /// <summary>A row somebody edited by hand into nonsense counts as no acknowledgement, not a broken page.</summary>
    private static Stored? Read(string value)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(value);
            return stored?.Date is null || stored.Errors is null ? null : stored;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
