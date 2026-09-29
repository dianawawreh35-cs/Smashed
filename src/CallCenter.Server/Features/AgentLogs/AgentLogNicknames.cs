using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.AgentLogs;

/// <summary>
/// What the supervisors call each laptop on the Logs page (S-64), so
/// "DESKTOP-RMSFSIV" can read as "Front desk".
/// </summary>
/// <remarks>
/// Kept in <c>settings</c>, one row per laptop under <see cref="Prefix"/>, as the
/// PBX switches keep theirs: no migration, and the Settings page lists only its
/// own catalogue, so these never show there. Not beside the log files, because
/// a laptop's folder goes when its last file passes retention, and the name
/// should still be there when the laptop comes back.
/// </remarks>
public class AgentLogNicknames(CallCenterDbContext db, TimeProvider clock)
{
    public const string Prefix = "agent_logs.nickname.";

    public const int MaxLength = 40;

    /// <summary>Every laptop's nickname, by laptop.</summary>
    public async Task<IReadOnlyDictionary<string, string>> AllAsync(CancellationToken ct) =>
        await db.Settings.AsNoTracking()
            .Where(s => s.Key.StartsWith(Prefix))
            .ToDictionaryAsync(s => s.Key[Prefix.Length..], s => s.Value, ct);

    /// <summary>Names the laptop, or with a blank name forgets its nickname. Returns the name kept.</summary>
    public async Task<string?> SetAsync(string laptop, string? nickname, Guid by, CancellationToken ct)
    {
        var key = Prefix + laptop;
        var name = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim();
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);

        if (name is null)
        {
            if (row is not null)
            {
                db.Settings.Remove(row);
            }
        }
        else
        {
            if (row is null)
            {
                row = new Setting { Key = key };
                db.Settings.Add(row);
            }

            row.Value = name;
            row.UpdatedAt = clock.GetUtcNow();
            row.UpdatedBy = by;
        }

        await db.SaveChangesAsync(ct);
        return name;
    }
}
