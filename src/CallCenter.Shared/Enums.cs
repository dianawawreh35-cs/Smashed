namespace CallCenter.Shared;

/// <summary>
/// The exact string values allowed by the CHECK constraints in
/// <c>docs/SCHEMA.md</c>. The database stores <c>text</c>, so these constants
/// are the single source of truth shared by the API, the Agent App and the SPA.
/// <b>Changing one means changing a CHECK constraint and writing a migration.</b>
/// </summary>
public static class CommunicationKinds
{
    /// <summary>A telephone call.</summary>
    public const string Call = "Call";

    /// <summary>An entry logged against a non-phone channel (WhatsApp, Facebook, ...).</summary>
    public const string App = "App";

    public static readonly IReadOnlyList<string> All = new[] { Call, App };
}

/// <summary><c>communications.direction</c>.</summary>
public static class Directions
{
    public const string In = "In";
    public const string Out = "Out";

    /// <summary>App entries have no direction.</summary>
    public const string None = "None";

    public static readonly IReadOnlyList<string> All = new[] { In, Out, None };
}

/// <summary><c>communications.status</c>.</summary>
public static class CommunicationStatuses
{
    public const string Ringing = "Ringing";
    public const string Answered = "Answered";
    public const string Missed = "Missed";
    public const string Rejected = "Rejected";
    public const string Blocked = "Blocked";
    public const string Abandoned = "Abandoned";
    public const string Overflowed = "Overflowed";
    public const string Failed = "Failed";

    /// <summary>An App entry or a manually logged item.</summary>
    public const string Logged = "Logged";

    public static readonly IReadOnlyList<string> All =
        new[] { Ringing, Answered, Missed, Rejected, Blocked, Abandoned, Overflowed, Failed, Logged };
}

/// <summary><c>communications.source</c>.</summary>
public static class CommunicationSources
{
    public const string AgentApp = "AgentApp";
    public const string Ami = "AMI";
    public const string Cdr = "CDR";
    public const string Manual = "Manual";

    public static readonly IReadOnlyList<string> All = new[] { AgentApp, Ami, Cdr, Manual };
}

/// <summary><c>users.role</c>.</summary>
public static class UserRoles
{
    public const string Agent = "Agent";
    public const string Supervisor = "Supervisor";

    public static readonly IReadOnlyList<string> All = new[] { Agent, Supervisor };
}

/// <summary><c>follow_up_tasks.status</c>.</summary>
public static class TaskStatuses
{
    public const string Open = "Open";
    public const string Done = "Done";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> All = new[] { Open, Done, Cancelled };
}

/// <summary><c>follow_up_tasks.created_from</c>.</summary>
public static class TaskOrigins
{
    public const string Complaint = "Complaint";
    public const string Abandoned = "Abandoned";
    public const string Missed = "Missed";
    public const string Manual = "Manual";

    public static readonly IReadOnlyList<string> All = new[] { Complaint, Abandoned, Missed, Manual };
}

/// <summary><c>pbx_events_raw.source</c>.</summary>
public static class PbxEventSources
{
    public const string Ami = "AMI";
    public const string Cdr = "CDR";
    public const string Sip = "SIP";

    public static readonly IReadOnlyList<string> All = new[] { Ami, Cdr, Sip };
}

// ---------------------------------------------------------------------------
// CLR enums
// ---------------------------------------------------------------------------

/// <summary>Kind of communication record. Persisted as <see cref="CommunicationKinds"/>.</summary>
public enum CommunicationKind
{
    Call,
    App,
}

/// <summary>Direction of a communication. Persisted as <see cref="Directions"/>.</summary>
public enum Direction
{
    In,
    Out,

    /// <summary>App entries carry no direction.</summary>
    None,
}

/// <summary>Outcome of a communication. Persisted as <see cref="CommunicationStatuses"/>.</summary>
public enum CommunicationStatus
{
    Ringing,
    Answered,
    Missed,
    Rejected,
    Blocked,
    Abandoned,
    Overflowed,
    Failed,
    Logged,
}

/// <summary>Where a communication record came from. Persisted as <see cref="CommunicationSources"/>.</summary>
public enum CommunicationSource
{
    /// <summary>Reported by the WPF Agent App.</summary>
    AgentApp,

    /// <summary>Live Asterisk Manager Interface event. Persisted as <c>"AMI"</c>.</summary>
    Ami,

    /// <summary>Imported from the PBX call detail records. Persisted as <c>"CDR"</c>.</summary>
    Cdr,

    /// <summary>Entered by hand by a supervisor.</summary>
    Manual,
}

/// <summary>Application role of a user. Persisted as <see cref="UserRoles"/>.</summary>
public enum UserRole
{
    Agent,
    Supervisor,
}

/// <summary>
/// Status of a follow-up task. Persisted as <see cref="TaskStatuses"/>.
/// </summary>
/// <remarks>
/// This name collides with <see cref="System.Threading.Tasks.TaskStatus"/>, which
/// implicit usings bring into scope. Outside the <c>CallCenter.Shared</c>
/// namespace, qualify it as <c>CallCenter.Shared.TaskStatus</c>.
/// </remarks>
public enum TaskStatus
{
    Open,
    Done,
    Cancelled,
}

/// <summary>What created a follow-up task. Persisted as <see cref="TaskOrigins"/>.</summary>
public enum TaskOrigin
{
    Complaint,
    Abandoned,
    Missed,
    Manual,
}

/// <summary>Origin of a raw PBX event. Persisted as <see cref="PbxEventSources"/>.</summary>
public enum PbxEventSource
{
    /// <summary>Persisted as <c>"AMI"</c>.</summary>
    Ami,

    /// <summary>Persisted as <c>"CDR"</c>.</summary>
    Cdr,

    /// <summary>Persisted as <c>"SIP"</c>.</summary>
    Sip,
}

/// <summary>
/// Conversion between the CLR enums above and the database string values.
/// </summary>
/// <remarks>
/// Every member name matches its database value except the acronyms
/// (<c>Ami</c>/<c>Cdr</c>/<c>Sip</c> against <c>AMI</c>/<c>CDR</c>/<c>SIP</c>),
/// so writing goes through the explicit <c>ToDbValue</c> overloads and reading
/// through a case-insensitive parse.
/// </remarks>
public static class EnumStrings
{
    public static string ToDbValue(this CommunicationKind value) => value switch
    {
        CommunicationKind.Call => CommunicationKinds.Call,
        CommunicationKind.App => CommunicationKinds.App,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string ToDbValue(this Direction value) => value switch
    {
        Direction.In => Directions.In,
        Direction.Out => Directions.Out,
        Direction.None => Directions.None,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string ToDbValue(this CommunicationStatus value) => value switch
    {
        CommunicationStatus.Ringing => CommunicationStatuses.Ringing,
        CommunicationStatus.Answered => CommunicationStatuses.Answered,
        CommunicationStatus.Missed => CommunicationStatuses.Missed,
        CommunicationStatus.Rejected => CommunicationStatuses.Rejected,
        CommunicationStatus.Blocked => CommunicationStatuses.Blocked,
        CommunicationStatus.Abandoned => CommunicationStatuses.Abandoned,
        CommunicationStatus.Overflowed => CommunicationStatuses.Overflowed,
        CommunicationStatus.Failed => CommunicationStatuses.Failed,
        CommunicationStatus.Logged => CommunicationStatuses.Logged,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string ToDbValue(this CommunicationSource value) => value switch
    {
        CommunicationSource.AgentApp => CommunicationSources.AgentApp,
        CommunicationSource.Ami => CommunicationSources.Ami,
        CommunicationSource.Cdr => CommunicationSources.Cdr,
        CommunicationSource.Manual => CommunicationSources.Manual,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string ToDbValue(this UserRole value) => value switch
    {
        UserRole.Agent => UserRoles.Agent,
        UserRole.Supervisor => UserRoles.Supervisor,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string ToDbValue(this TaskStatus value) => value switch
    {
        TaskStatus.Open => TaskStatuses.Open,
        TaskStatus.Done => TaskStatuses.Done,
        TaskStatus.Cancelled => TaskStatuses.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string ToDbValue(this TaskOrigin value) => value switch
    {
        TaskOrigin.Complaint => TaskOrigins.Complaint,
        TaskOrigin.Abandoned => TaskOrigins.Abandoned,
        TaskOrigin.Missed => TaskOrigins.Missed,
        TaskOrigin.Manual => TaskOrigins.Manual,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string ToDbValue(this PbxEventSource value) => value switch
    {
        PbxEventSource.Ami => PbxEventSources.Ami,
        PbxEventSource.Cdr => PbxEventSources.Cdr,
        PbxEventSource.Sip => PbxEventSources.Sip,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Parses a database string into <typeparamref name="TEnum"/>, ignoring case.</summary>
    public static TEnum Parse<TEnum>(string? value) where TEnum : struct, Enum =>
        TryParse<TEnum>(value, out var parsed)
            ? parsed
            : throw new ArgumentException($"'{value}' is not a valid {typeof(TEnum).Name} value.", nameof(value));

    /// <summary>Attempts to parse a database string into <typeparamref name="TEnum"/>.</summary>
    public static bool TryParse<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        // Enum.TryParse also accepts the underlying number ("1"), which would
        // silently turn a bad column value into a valid member. Database values
        // are always names, so reject anything numeric.
        if (!char.IsLetter(trimmed[0]))
        {
            return false;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out result) && Enum.IsDefined(result);
    }
}
