using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared.Contracts.Classifications;
using CallCenter.Shared.Text;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Classifications;

/// <summary>
/// The list of things a call can be about, as the supervisor maintains it
/// (S-40).
/// </summary>
/// <remarks>
/// <b>Name and label are different things, deliberately.</b> The name is the
/// stable key — <c>Order</c>, <c>Complaint</c> — and it is what the reports and
/// the form's <c>showWhenType</c> rules are written against. The labels are what
/// agents read, in Arabic and English. A supervisor renaming "Complaint" to
/// "Issue" changes the label and nothing else breaks, which is the same rule
/// S-41 states for branches: everything holds the id, never the name.
///
/// <b>Nothing in use is ever deleted.</b> Deleting a type that calls already
/// carry would leave those calls describing nothing, and the reports would lose
/// history that cannot be rebuilt. A type in use is hidden instead, which stops
/// it appearing on new classifications while old ones still read back correctly.
/// The six the system was built around cannot be deleted at all.
/// </remarks>
public class ClassificationTypeService(
    CallCenterDbContext db, ILogger<ClassificationTypeService> logger)
{
    public async Task<IReadOnlyList<ClassificationTypeDto>> ListAsync(
        bool includeInactive = false, CancellationToken ct = default)
    {
        var inUse = await db.Classifications.Select(c => c.TypeId).Distinct().ToListAsync(ct);

        return await db.ClassificationTypes
            .AsNoTracking()
            .Where(t => includeInactive || t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.LabelEn)
            .Select(t => new ClassificationTypeDto(
                t.Id, t.Name, t.LabelAr, t.LabelEn, t.Colour,
                t.IsSystem, t.SortOrder, t.IsActive, inUse.Contains(t.Id)))
            .ToListAsync(ct);
    }

    public async Task<(ClassificationTypeDto? Type, ClassificationService.Failure? Failure)> CreateAsync(
        UpsertClassificationTypeRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var labelAr = request.LabelAr.Trim();
        var labelEn = request.LabelEn.Trim();

        if (labelAr.Length == 0 || labelEn.Length == 0)
        {
            return (null, ClassificationService.Failure.BadName);
        }

        // The key is derived from the English label when none is given, because
        // a supervisor should not have to think about keys at all - but it is
        // stored, and from then on it never changes.
        var name = (request.Name ?? labelEn).Trim();
        name = new string(name.Where(char.IsLetterOrDigit).ToArray());

        if (name.Length == 0)
        {
            return (null, ClassificationService.Failure.BadName);
        }

        if (await db.ClassificationTypes.AnyAsync(t => t.Name == name, ct))
        {
            return (null, ClassificationService.Failure.BadName);
        }

        var type = new ClassificationType
        {
            Name = name,
            LabelAr = labelAr,
            LabelEn = labelEn,
            Colour = request.Colour,
            IsSystem = false,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
        };

        db.ClassificationTypes.Add(type);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Classification type {Name} added by {UserId}", type.Name, actingUserId);

        return (new ClassificationTypeDto(
            type.Id, type.Name, type.LabelAr, type.LabelEn, type.Colour,
            type.IsSystem, type.SortOrder, type.IsActive, false), null);
    }

    /// <summary>
    /// Renames a type, recolours it, reorders it or hides it (S-40).
    /// </summary>
    /// <remarks>
    /// The key is not touched, whatever is sent. It is what the reports are
    /// written against, and letting it move would break them silently a year
    /// after the rename.
    /// </remarks>
    public async Task<(ClassificationTypeDto? Type, ClassificationService.Failure? Failure)> UpdateAsync(
        Guid id, UpsertClassificationTypeRequest request, Guid actingUserId,
        CancellationToken ct = default)
    {
        var type = await db.ClassificationTypes.FirstOrDefaultAsync(t => t.Id == id, ct);

        if (type is null)
        {
            return (null, ClassificationService.Failure.UnknownType);
        }

        var labelAr = request.LabelAr.Trim();
        var labelEn = request.LabelEn.Trim();

        if (labelAr.Length == 0 || labelEn.Length == 0)
        {
            return (null, ClassificationService.Failure.BadName);
        }

        type.LabelAr = labelAr;
        type.LabelEn = labelEn;
        type.Colour = request.Colour;
        type.SortOrder = request.SortOrder;
        type.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);

        var inUse = await db.Classifications.AnyAsync(c => c.TypeId == id, ct);

        return (new ClassificationTypeDto(
            type.Id, type.Name, type.LabelAr, type.LabelEn, type.Colour,
            type.IsSystem, type.SortOrder, type.IsActive, inUse), null);
    }

    /// <summary>
    /// Removes a type that nothing uses (S-40).
    /// </summary>
    /// <remarks>
    /// Refused for a type any call carries, and for the six the system was built
    /// around. The screen offers hiding instead, which is what a supervisor
    /// retiring a type actually wants: it stops appearing on new calls while the
    /// old ones still say what they were about.
    /// </remarks>
    public async Task<ClassificationService.Failure?> DeleteAsync(
        Guid id, CancellationToken ct = default)
    {
        var type = await db.ClassificationTypes.FirstOrDefaultAsync(t => t.Id == id, ct);

        if (type is null)
        {
            return ClassificationService.Failure.UnknownType;
        }

        if (type.IsSystem || await db.Classifications.AnyAsync(c => c.TypeId == id, ct))
        {
            return ClassificationService.Failure.TypeInUse;
        }

        db.ClassificationTypes.Remove(type);
        await db.SaveChangesAsync(ct);

        return null;
    }
}
