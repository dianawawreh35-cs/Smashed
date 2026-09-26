using CallCenter.Server.Data;
using CallCenter.Server.Features.Auth;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Pbx;

/// <summary>
/// The server's own extension on the PBX, which every feature-code call is
/// placed from: the blacklist's <c>*30</c> and <c>*31</c> (S-46) and the queue's
/// <c>*280</c> (S-60).
/// </summary>
/// <remarks>
/// Entered on the PBX blacklist card of the settings screen, and stored in the
/// <c>settings</c> table outside the catalogue, with the password encrypted by
/// the key the SIP secrets use (N-05). The PBX address is <c>pbx.host</c>, the
/// one the Agent Apps register to, falling back to configuration as sign-in
/// does.
/// </remarks>
public class PbxFeatureLine(CallCenterDbContext db, ISipSecretProtector protector, IConfiguration configuration)
{
    public static class Keys
    {
        public const string Extension = "pbx.features.extension";
        public const string Secret = "pbx.features.secret";

        public const string Prefix = "pbx.features.";
    }

    /// <summary>The extension as stored: its number (blank when unset) and whether a password is saved.</summary>
    public async Task<(string Extension, bool SecretSet)> StoredAsync(CancellationToken ct = default)
    {
        var stored = await LoadAsync(ct);
        return (stored.GetValueOrDefault(Keys.Extension) ?? string.Empty,
            !string.IsNullOrEmpty(stored.GetValueOrDefault(Keys.Secret)));
    }

    /// <summary>What to dial from; null until the extension, its password and the PBX address are all set.</summary>
    public async Task<PbxExtension?> GetAsync(CancellationToken ct = default)
    {
        var stored = await LoadAsync(ct);
        var extension = stored.GetValueOrDefault(Keys.Extension);
        var secret = protector.Unprotect(stored.GetValueOrDefault(Keys.Secret));

        if (string.IsNullOrWhiteSpace(extension) || string.IsNullOrEmpty(secret))
        {
            return null;
        }

        var host = await db.Settings.AsNoTracking()
            .Where(s => s.Key == "pbx.host")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(host))
        {
            host = configuration["Sip:Server"];
        }

        return string.IsNullOrWhiteSpace(host) ? null : new PbxExtension(host.Trim(), extension, secret);
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct) =>
        await db.Settings.AsNoTracking()
            .Where(s => s.Key.StartsWith(Keys.Prefix))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);
}
