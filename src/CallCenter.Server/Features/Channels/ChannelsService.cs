using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared.Contracts.Communications;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Channels;

/// <summary>
/// The list of ways a customer reaches the restaurant, as the supervisor
/// maintains it (S-41, the channel half): Phone, and the apps.
/// </summary>
/// <remarks>
/// <b>Renaming is safe</b> because every communication holds the channel's id,
/// never its name — the rule S-41 states for branches. A supervisor renaming
/// "Wheels" to "Wheels Delivery" changes what every screen prints and moves
/// nothing.
///
/// <b>Phone is a system channel.</b> Every call is filed under it by name
/// (<c>ChannelNames.Phone</c>), so it can be neither renamed nor hidden; the
/// Agent App would otherwise stop logging calls with a 500 nobody could explain.
///
/// <b>Nothing is deleted.</b> A channel with messages on it can be hidden, which
/// stops it being offered for new ones while the old ones still say where they
/// came from. There is no delete at all: a hidden channel with nothing on it
/// costs nothing, and a delete that works only sometimes is a screen that
/// argues with the supervisor.
/// </remarks>
public class ChannelsService(CallCenterDbContext db, ILogger<ChannelsService> logger)
{
    public enum Failure
    {
        /// <summary>No such channel.</summary>
        NotFound,

        /// <summary>The name is blank, or another channel already has it.</summary>
        BadName,

        /// <summary>Phone: it cannot be renamed or hidden.</summary>
        SystemChannel,
    }

    /// <summary>Every channel in the supervisor's order, hidden ones included when asked.</summary>
    public async Task<IReadOnlyList<ChannelDto>> ListAsync(
        bool includeInactive = false, CancellationToken ct = default)
    {
        var inUse = await db.Communications.Select(c => c.ChannelId).Distinct().ToListAsync(ct);

        return await db.Channels
            .AsNoTracking()
            .Where(c => includeInactive || c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new ChannelDto(c.Id, c.Name, c.IsSystem, c.SortOrder, c.IsActive, inUse.Contains(c.Id)))
            .ToListAsync(ct);
    }

    public async Task<(ChannelDto? Channel, Failure? Failure)> CreateAsync(
        UpsertChannelRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var name = request.Name.Trim();

        if (name.Length == 0 || await NameTakenAsync(name, null, ct))
        {
            return (null, Failure.BadName);
        }

        var channel = new Channel
        {
            Name = name,
            IsSystem = false,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
        };

        db.Channels.Add(channel);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Channel {Name} added by {UserId}", name, actingUserId);

        return (new ChannelDto(channel.Id, channel.Name, false, channel.SortOrder, channel.IsActive, false), null);
    }

    /// <summary>Renames, reorders or hides a channel (S-41). Phone accepts only a new position.</summary>
    public async Task<(ChannelDto? Channel, Failure? Failure)> UpdateAsync(
        Guid id, UpsertChannelRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == id, ct);

        if (channel is null)
        {
            return (null, Failure.NotFound);
        }

        var name = request.Name.Trim();

        if (name.Length == 0 || await NameTakenAsync(name, id, ct))
        {
            return (null, Failure.BadName);
        }

        if (channel.IsSystem && (name != channel.Name || !request.IsActive))
        {
            return (null, Failure.SystemChannel);
        }

        channel.Name = name;
        channel.SortOrder = request.SortOrder;
        channel.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Channel {Id} is now {Name} ({Active}) by {UserId}",
            id, name, request.IsActive ? "shown" : "hidden", actingUserId);

        var inUse = await db.Communications.AnyAsync(c => c.ChannelId == id, ct);

        return (new ChannelDto(channel.Id, channel.Name, channel.IsSystem, channel.SortOrder, channel.IsActive, inUse), null);
    }

    /// <summary>Case-insensitive: "whatsapp" beside "WhatsApp" is two names for one thing.</summary>
    private Task<bool> NameTakenAsync(string name, Guid? except, CancellationToken ct) =>
        db.Channels.AnyAsync(c => c.Id != except && EF.Functions.ILike(c.Name, name), ct);
}
