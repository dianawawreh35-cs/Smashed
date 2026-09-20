using System.Text.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared.Contracts.Settings;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Settings;

/// <summary>
/// Reading and changing the values the system uses at runtime (S-47).
/// </summary>
public class SettingsService(CallCenterDbContext db, ILogger<SettingsService> logger)
{
    /// <summary>Everything the supervisor may change, in catalogue order.</summary>
    /// <summary>
    /// A whole-number setting, or <paramref name="fallback"/> when it is unset
    /// or unreadable.
    /// </summary>
    /// <remarks>
    /// Never throws. A settings row somebody has edited by hand into nonsense
    /// must not take a screen down; the default is always a sane value, and the
    /// supervisor can correct it in the app.
    /// </remarks>
    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken ct = default)
    {
        var value = await db.Settings
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        if (int.TryParse(value, out var parsed))
        {
            return parsed;
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            logger.LogWarning(
                "Setting {Key} is {Value}, which is not a whole number; using {Fallback}",
                key, value, fallback);
        }

        return fallback;
    }

    public async Task<IReadOnlyList<SettingDto>> ListAsync(CancellationToken ct = default)
    {
        var stored = await db.Settings
            .AsNoTracking()
            .Include(s => s.UpdatedByUser)
            .ToDictionaryAsync(s => s.Key, s => s, StringComparer.OrdinalIgnoreCase, ct);

        return SettingsCatalog.All
            .Select(definition =>
            {
                stored.TryGetValue(definition.Key, out var row);
                return new SettingDto(
                    definition.Key,
                    row?.Value ?? string.Empty,
                    definition.Kind,
                    definition.Options,
                    row?.UpdatedAt ?? default,
                    row?.UpdatedByUser?.DisplayName);
            })
            .ToList();
    }

    /// <summary>
    /// Applies a batch of changes (S-47). All or nothing: one bad value rejects
    /// the whole request, so a screen saving six fields never half-applies.
    /// </summary>
    /// <returns>The problems found, keyed by setting; empty when the write succeeded.</returns>
    public async Task<IReadOnlyDictionary<string, string>> UpdateAsync(
        IReadOnlyDictionary<string, string> values, Guid actingUserId, CancellationToken ct = default)
    {
        var problems = new Dictionary<string, string>();

        foreach (var (key, value) in values)
        {
            var definition = SettingsCatalog.Find(key);
            if (definition is null)
            {
                problems[key] = "is not a setting this system uses";
                continue;
            }

            var problem = definition.Validate(value?.Trim() ?? string.Empty);
            if (problem is not null)
            {
                problems[key] = problem;
            }
        }

        if (problems.Count > 0)
        {
            return problems;
        }

        var stored = await db.Settings
            .Where(s => values.Keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, StringComparer.OrdinalIgnoreCase, ct);

        var changed = new List<(string Key, string? Before, string After)>();

        foreach (var (key, rawValue) in values)
        {
            // Catalogue lookup, so the stored key keeps its canonical spelling
            // even if the caller sent a different case.
            var canonicalKey = SettingsCatalog.Find(key)!.Key;
            var value = rawValue?.Trim() ?? string.Empty;

            if (stored.TryGetValue(canonicalKey, out var row))
            {
                if (row.Value == value)
                {
                    continue;
                }

                changed.Add((canonicalKey, row.Value, value));
                row.Value = value;
                row.UpdatedBy = actingUserId;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                // A setting the seed predates. Create it rather than refuse.
                changed.Add((canonicalKey, null, value));
                db.Settings.Add(new Setting
                {
                    Key = canonicalKey,
                    Value = value,
                    UpdatedBy = actingUserId,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        if (changed.Count == 0)
        {
            return problems;
        }

        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = actingUserId,
            Entity = "settings",
            Action = "update",
            Before = JsonSerializer.SerializeToDocument(
                changed.ToDictionary(c => c.Key, c => c.Before)),
            After = JsonSerializer.SerializeToDocument(
                changed.ToDictionary(c => c.Key, c => c.After)),
        });

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Settings changed: {Keys}", string.Join(", ", changed.Select(c => c.Key)));

        return problems;
    }
}
