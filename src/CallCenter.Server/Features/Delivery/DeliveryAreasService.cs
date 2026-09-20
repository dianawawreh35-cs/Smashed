using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared.Contracts.Delivery;
using CallCenter.Shared.Text;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Delivery;

/// <summary>
/// Where the restaurant delivers, which branch covers it, and what it costs
/// (A-65, S-58).
/// </summary>
/// <remarks>
/// Agents read this mid-call to answer "which branch is this, and how much is
/// delivery?". Supervisors maintain it, usually by pasting a branch's list
/// wholesale out of the spreadsheet it already lives in.
///
/// Names are matched on the normalised form, the same folding contact names get
/// (<see cref="NameNormalizer"/>). These are Arabic place names copied from
/// handwritten lists by several people, so <c>الطيرة</c> and <c>الطيره</c> have to
/// be the same place — otherwise an agent searches, finds nothing, and quotes
/// the wrong price.
/// </remarks>
public class DeliveryAreasService(CallCenterDbContext db, ILogger<DeliveryAreasService> logger)
{
    /// <summary>How many results a search returns. A lookup, not a report.</summary>
    public const int SearchLimit = 50;

    public enum Failure
    {
        NotFound,

        /// <summary>No branch with that id, or it is disabled.</summary>
        UnknownBranch,

        /// <summary>Another area already has this name (see the class remarks).</summary>
        DuplicateName,

        /// <summary>The name was blank once trimmed.</summary>
        NoName,
    }

    /// <summary>
    /// Areas matching what the agent typed (A-65).
    /// </summary>
    /// <remarks>
    /// A substring match on the normalised name, not an exact one: an agent
    /// hears "Ein Munjid" and types part of it, and a lookup that only answers
    /// on the whole name is a lookup nobody uses. Inactive areas are left out —
    /// the agent is asking what to charge today.
    /// </remarks>
    public async Task<IReadOnlyList<DeliveryAreaDto>> SearchAsync(
        string? query, Guid? branchId = null, bool includeInactive = false,
        CancellationToken ct = default)
    {
        var areas = db.DeliveryAreas.AsNoTracking().AsQueryable();

        if (!includeInactive)
        {
            areas = areas.Where(a => a.IsActive);
        }

        if (branchId is { } branch)
        {
            areas = areas.Where(a => a.BranchId == branch);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalised = NameNormalizer.Normalize(query);

            if (normalised.Length > 0)
            {
                // ILike, matching the contact search. Both sides are already
                // lowercased by NameNormalizer, so Like would give the same
                // answer today - but that is an invariant declared in another
                // file, and relying on it means this search breaks silently if
                // the normaliser ever stops folding case.
                areas = areas.Where(a => EF.Functions.ILike(a.NameNormalised, $"%{normalised}%"));
            }
        }

        return await areas
            .OrderBy(a => a.Name)
            .Take(SearchLimit)
            .Select(a => new DeliveryAreaDto(
                a.Id, a.Name, a.BranchId, a.Branch.Name, a.Price, a.IsActive))
            .ToListAsync(ct);
    }

    /// <summary>Creates one area (S-58).</summary>
    public async Task<(DeliveryAreaDto? Area, Failure? Failure)> CreateAsync(
        UpsertDeliveryAreaRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        var normalised = NameNormalizer.Normalize(name);

        if (normalised.Length == 0)
        {
            return (null, Failure.NoName);
        }

        if (!await db.Branches.AnyAsync(b => b.Id == request.BranchId, ct))
        {
            return (null, Failure.UnknownBranch);
        }

        if (await db.DeliveryAreas.AnyAsync(a => a.NameNormalised == normalised, ct))
        {
            return (null, Failure.DuplicateName);
        }

        var area = new DeliveryArea
        {
            Name = name,
            NameNormalised = normalised,
            BranchId = request.BranchId,
            Price = request.Price,
            IsActive = request.IsActive,
            CreatedBy = actingUserId,
            UpdatedBy = actingUserId,
        };

        db.DeliveryAreas.Add(area);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Delivery area {Name} added for branch {BranchId}", name, request.BranchId);

        return (await GetAsync(area.Id, ct), null);
    }

    /// <summary>Changes an area's name, branch, price or whether it is active (S-58).</summary>
    public async Task<(DeliveryAreaDto? Area, Failure? Failure)> UpdateAsync(
        Guid id, UpsertDeliveryAreaRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var area = await db.DeliveryAreas.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (area is null)
        {
            return (null, Failure.NotFound);
        }

        var name = request.Name.Trim();
        var normalised = NameNormalizer.Normalize(name);

        if (normalised.Length == 0)
        {
            return (null, Failure.NoName);
        }

        if (!await db.Branches.AnyAsync(b => b.Id == request.BranchId, ct))
        {
            return (null, Failure.UnknownBranch);
        }

        if (await db.DeliveryAreas.AnyAsync(a => a.NameNormalised == normalised && a.Id != id, ct))
        {
            return (null, Failure.DuplicateName);
        }

        area.Name = name;
        area.NameNormalised = normalised;
        area.BranchId = request.BranchId;
        area.Price = request.Price;
        area.IsActive = request.IsActive;
        area.UpdatedBy = actingUserId;
        area.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return (await GetAsync(id, ct), null);
    }

    /// <summary>
    /// Removes an area (S-58).
    /// </summary>
    /// <remarks>
    /// A real delete, unlike contacts. Nothing references a delivery area — no
    /// call, no order, no report — so removing one loses nothing but the row.
    /// The supervisor who wants it back can also switch it inactive instead,
    /// which is why both exist.
    /// </remarks>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var removed = await db.DeliveryAreas.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
        return removed > 0;
    }

    public async Task<DeliveryAreaDto?> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.DeliveryAreas
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new DeliveryAreaDto(
                a.Id, a.Name, a.BranchId, a.Branch.Name, a.Price, a.IsActive))
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Adds a whole branch's list at once, pasted from a spreadsheet (S-58).
    /// </summary>
    /// <remarks>
    /// Every line is parsed before anything is written, and the whole import is
    /// one transaction: a paste that is half-rejected must not leave the branch
    /// half-updated, because the supervisor's next move would be to paste it
    /// again and they need the first attempt to have changed nothing.
    ///
    /// A line naming an area that belongs to <b>another</b> branch is rejected
    /// rather than moved. Moving it silently is how an area ends up served by
    /// whichever branch was pasted last, and the supervisor would not see it
    /// happen.
    /// </remarks>
    public async Task<(ImportDeliveryAreasResult? Result, Failure? Failure)> ImportAsync(
        ImportDeliveryAreasRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        if (!await db.Branches.AnyAsync(b => b.Id == request.BranchId, ct))
        {
            return (null, Failure.UnknownBranch);
        }

        var problems = new List<ImportProblem>();
        var parsed = new Dictionary<string, (string Name, decimal Price)>();

        var lines = request.Lines.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var number = i + 1;

            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            // Tab first: that is what a paste out of Excel gives. Comma after,
            // for a list typed by hand or saved as CSV.
            var parts = raw.Contains('\t')
                ? raw.Split('\t')
                : raw.Split(',');

            if (parts.Length < 2)
            {
                problems.Add(new ImportProblem(number, raw.Trim(), "no_price"));
                continue;
            }

            var name = parts[0].Trim();
            if (name.Length == 0)
            {
                problems.Add(new ImportProblem(number, raw.Trim(), "no_name"));
                continue;
            }

            // The last field, not the second: a name containing a comma leaves
            // the price at the end either way.
            var priceText = parts[^1].Trim();

            if (!decimal.TryParse(priceText, out var price) || price < 0)
            {
                problems.Add(new ImportProblem(number, raw.Trim(), "bad_price", priceText));
                continue;
            }

            var normalised = NameNormalizer.Normalize(name);
            if (normalised.Length == 0)
            {
                problems.Add(new ImportProblem(number, raw.Trim(), "no_name"));
                continue;
            }

            if (parsed.ContainsKey(normalised))
            {
                problems.Add(new ImportProblem(number, raw.Trim(), "duplicate_in_paste", name));
                continue;
            }

            parsed[normalised] = (name, price);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var removed = 0;

        if (request.Replace)
        {
            removed = await db.DeliveryAreas
                .Where(a => a.BranchId == request.BranchId)
                .ExecuteDeleteAsync(ct);
        }

        // Everything with one of these names, whichever branch it belongs to —
        // the unique index is global, so a name held by another branch has to be
        // reported rather than silently stolen.
        var names = parsed.Keys.ToList();

        var existing = await db.DeliveryAreas
            .Where(a => names.Contains(a.NameNormalised))
            .ToListAsync(ct);

        var byName = existing.ToDictionary(a => a.NameNormalised);

        var added = 0;
        var updated = 0;

        foreach (var (normalised, (name, price)) in parsed)
        {
            if (byName.TryGetValue(normalised, out var area))
            {
                if (area.BranchId != request.BranchId)
                {
                    problems.Add(new ImportProblem(0, name, "other_branch"));
                    continue;
                }

                area.Name = name;
                area.Price = price;
                area.IsActive = true;
                area.UpdatedBy = actingUserId;
                area.UpdatedAt = DateTimeOffset.UtcNow;
                updated++;
                continue;
            }

            db.DeliveryAreas.Add(new DeliveryArea
            {
                Name = name,
                NameNormalised = normalised,
                BranchId = request.BranchId,
                Price = price,
                CreatedBy = actingUserId,
                UpdatedBy = actingUserId,
            });

            added++;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Delivery import for branch {BranchId}: {Added} added, {Updated} updated, {Removed} removed, {Problems} rejected",
            request.BranchId, added, updated, removed, problems.Count);

        return (new ImportDeliveryAreasResult(added, updated, removed, problems), null);
    }
}
