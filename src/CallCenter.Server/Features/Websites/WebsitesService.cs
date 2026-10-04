using System.Text.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Websites;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Websites;

/// <summary>
/// The websites the agents work in, as tabs inside the Agent App (A-88), as the
/// supervisor lists them.
/// </summary>
/// <remarks>
/// <b>The password is stored encrypted</b>, with the same key and the same
/// AES-GCM as the extensions' SIP secrets (N-05): a database dump does not hand
/// it over. It leaves the server only for the Agent App, which types it into
/// the site. The supervisor's page is told only whether there is one.
///
/// <b>Deleting is allowed.</b> Nothing else refers to a website: a tab removed
/// is gone from every laptop at its next sign-in, and what it was is kept in
/// the audit log (N-06), without the password.
/// </remarks>
public class WebsitesService(
    CallCenterDbContext db, ISipSecretProtector secrets, ILogger<WebsitesService> logger)
{
    /// <summary>What the audit log calls these rows.</summary>
    public const string AuditEntity = "website";

    /// <summary>Where the number goes in a cart address (A-85).</summary>
    public const string NumberPlaceholder = "{number}";

    public enum Failure
    {
        NotFound,

        /// <summary>The Arabic or the English name is blank.</summary>
        BadName,

        /// <summary>The address is not a whole http or https address.</summary>
        BadUrl,

        /// <summary>The cart address is not one, or has no <c>{number}</c>.</summary>
        BadCartUrl,

        /// <summary>Another tab already opens the caller's cart.</summary>
        CartTaken,

        /// <summary>Not a known login kind, or a shared login with no username.</summary>
        BadLogin,
    }

    /// <summary>Every tab in the supervisor's order, hidden ones too, for the Websites page.</summary>
    public async Task<IReadOnlyList<WebsiteDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.Websites.AsNoTracking()
            .OrderBy(w => w.SortOrder).ThenBy(w => w.NameEn)
            .ToListAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    /// <summary>
    /// The tabs on show, for the Agent App at sign-in, with the shared logins'
    /// passwords in the clear. A password the key cannot read comes as none,
    /// and the tab is left for the agent to sign in to by hand.
    /// </summary>
    public async Task<IReadOnlyList<AgentWebsiteDto>> ForAgentAsync(CancellationToken ct = default)
    {
        var rows = await db.Websites.AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.SortOrder).ThenBy(w => w.NameEn)
            .ToListAsync(ct);

        return rows.Select(w => new AgentWebsiteDto(
                w.Id, w.NameAr, w.NameEn, w.Url, w.Login,
                w.Login == WebsiteLogins.Shared ? w.Username : null,
                w.Login == WebsiteLogins.Shared ? secrets.Unprotect(w.PasswordSecret) : null,
                w.AlertsWithSound, w.CartUrl, w.UsernameSelector, w.PasswordSelector, w.SubmitSelector))
            .ToList();
    }

    public async Task<(WebsiteDto? Website, Failure? Failure)> CreateAsync(
        UpsertWebsiteRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        if (await CheckAsync(request, null, ct) is { } failure)
        {
            return (null, failure);
        }

        // The id made here, not by the database: the audit row below needs it
        // before the insert.
        var website = new Website { Id = Guid.NewGuid() };
        Apply(website, request);

        db.Websites.Add(website);
        db.AuditLog.Add(Audit(actingUserId, website, "create", null, Snapshot(website, request.Password is { Length: > 0 })));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Website {Name} added by {UserId}", website.NameEn, actingUserId);

        return (ToDto(website), null);
    }

    public async Task<(WebsiteDto? Website, Failure? Failure)> UpdateAsync(
        Guid id, UpsertWebsiteRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var website = await db.Websites.FirstOrDefaultAsync(w => w.Id == id, ct);

        if (website is null)
        {
            return (null, Failure.NotFound);
        }

        if (await CheckAsync(request, id, ct) is { } failure)
        {
            return (null, failure);
        }

        var before = Snapshot(website, false);
        Apply(website, request);
        db.AuditLog.Add(Audit(actingUserId, website, "update", before, Snapshot(website, request.Password is not null)));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Website {Id} ({Name}) changed by {UserId}", id, website.NameEn, actingUserId);

        return (ToDto(website), null);
    }

    public async Task<Failure?> DeleteAsync(Guid id, Guid actingUserId, CancellationToken ct = default)
    {
        var website = await db.Websites.FirstOrDefaultAsync(w => w.Id == id, ct);

        if (website is null)
        {
            return Failure.NotFound;
        }

        db.Websites.Remove(website);
        db.AuditLog.Add(Audit(actingUserId, website, "delete", Snapshot(website, false), null));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Website {Id} ({Name}) removed by {UserId}", id, website.NameEn, actingUserId);

        return null;
    }

    private async Task<Failure?> CheckAsync(UpsertWebsiteRequest request, Guid? id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NameAr) || string.IsNullOrWhiteSpace(request.NameEn))
        {
            return Failure.BadName;
        }

        if (!IsWebAddress(request.Url))
        {
            return Failure.BadUrl;
        }

        if (!WebsiteLogins.All.Contains(request.Login)
            || (request.Login == WebsiteLogins.Shared && string.IsNullOrWhiteSpace(request.Username)))
        {
            return Failure.BadLogin;
        }

        if (Blank(request.CartUrl) is { } cart)
        {
            if (!cart.Contains(NumberPlaceholder, StringComparison.OrdinalIgnoreCase)
                || !IsWebAddress(cart.Replace(NumberPlaceholder, "0", StringComparison.OrdinalIgnoreCase)))
            {
                return Failure.BadCartUrl;
            }

            // One tab takes the cart; two would leave the app guessing which.
            if (await db.Websites.AnyAsync(w => w.Id != id && w.CartUrl != null, ct))
            {
                return Failure.CartTaken;
            }
        }

        return null;
    }

    private void Apply(Website website, UpsertWebsiteRequest request)
    {
        website.NameAr = request.NameAr.Trim();
        website.NameEn = request.NameEn.Trim();
        website.Url = request.Url.Trim();
        website.Login = request.Login;
        website.AlertsWithSound = request.AlertsWithSound;
        website.CartUrl = Blank(request.CartUrl);
        website.UsernameSelector = Blank(request.UsernameSelector);
        website.PasswordSelector = Blank(request.PasswordSelector);
        website.SubmitSelector = Blank(request.SubmitSelector);
        website.SortOrder = request.SortOrder;
        website.IsActive = request.IsActive;
        website.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.Login == WebsiteLogins.Shared)
        {
            website.Username = request.Username!.Trim();

            if (request.Password is not null)
            {
                website.PasswordSecret = request.Password.Length == 0 ? null : secrets.Protect(request.Password);
            }
        }
        else
        {
            // An agent's own login: nothing of the supervisor's is kept for it.
            website.Username = null;
            website.PasswordSecret = null;
        }
    }

    private static bool IsWebAddress(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static WebsiteDto ToDto(Website w) => new(
        w.Id, w.NameAr, w.NameEn, w.Url, w.Login, w.Username, w.PasswordSecret is not null,
        w.AlertsWithSound, w.CartUrl, w.UsernameSelector, w.PasswordSelector, w.SubmitSelector,
        w.SortOrder, w.IsActive);

    /// <summary>The row for the audit log. Never the password: only whether it changed.</summary>
    private static object Snapshot(Website w, bool passwordChanged) => new
    {
        nameAr = w.NameAr,
        nameEn = w.NameEn,
        url = w.Url,
        login = w.Login,
        username = w.Username,
        passwordChanged,
        alertsWithSound = w.AlertsWithSound,
        cartUrl = w.CartUrl,
        sortOrder = w.SortOrder,
        isActive = w.IsActive,
    };

    private static AuditLogEntry Audit(Guid userId, Website w, string action, object? before, object? after) => new()
    {
        UserId = userId,
        At = DateTimeOffset.UtcNow,
        Entity = AuditEntity,
        EntityId = w.Id.ToString(),
        Action = action,
        Before = before is null ? null : JsonSerializer.SerializeToDocument(before),
        After = after is null ? null : JsonSerializer.SerializeToDocument(after),
    };
}
