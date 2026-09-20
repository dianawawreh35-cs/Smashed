using CallCenter.Shared;

namespace CallCenter.Server.Data.Seed;

/// <summary>
/// The starting contents of a new database, from section 7 of
/// <c>docs/SCHEMA.md</c>. Data only — <see cref="DatabaseSeeder"/> applies it.
/// </summary>
public static class SeedData
{
    /// <summary>Renamed by the supervisor after installation (S-41).</summary>
    public static readonly string[] Branches =
    [
        "Branch 1", "Branch 2", "Branch 3", "Branch 4",
    ];

    /// <summary>
    /// Phone is a system channel: calls are always attributed to it, and it
    /// cannot be deleted. The rest are the app channels from A-70, and the
    /// supervisor may add more.
    /// </summary>
    public static readonly (string Name, bool IsSystem)[] Channels =
    [
        (ChannelNames.Phone, true),
        ("WhatsApp", false),
        ("Facebook", false),
        ("Instagram", false),
        ("Wheels", false),
    ];

    /// <summary>
    /// The classification types from A-40. Order and Complaint are system types
    /// because reports depend on them existing (R-05, R-14, R-17).
    /// </summary>
    public static readonly (string Name, string LabelAr, string LabelEn, string Colour, bool IsSystem)[] ClassificationTypes =
    [
        ("Order",        "طلب",       "Order",        "#16a34a", true),
        ("Cancellation", "إلغاء",     "Cancellation", "#f97316", false),
        ("Complaint",    "شكوى",      "Complaint",    "#dc2626", true),
        ("Inquiry",      "استفسار",   "Inquiry",      "#2563eb", false),
        ("WrongNumber",  "رقم خاطئ",  "Wrong number", "#6b7280", false),
        ("Other",        "أخرى",      "Other",        "#8b5cf6", false),
    ];

    /// <summary>
    /// Form version 1: the default fields from A-40, without the example
    /// complaint-reason field in the schema document — the supervisor adds
    /// custom fields themselves (S-40).
    /// </summary>
    public const string FormDefinitionV1 = """
        {
          "fields": [
            { "key": "type", "kind": "type", "required": true },
            { "key": "branch", "kind": "branch", "required": true },
            { "key": "order_value", "kind": "number",
              "label": { "ar": "قيمة الطلب", "en": "Order value" },
              "required": false,
              "showWhenType": ["Order", "Cancellation"] },
            { "key": "notes", "kind": "textarea",
              "label": { "ar": "ملاحظات", "en": "Notes" },
              "required": false },
            { "key": "follow_up", "kind": "checkbox",
              "label": { "ar": "متابعة", "en": "Follow-up required" } }
          ]
        }
        """;

    /// <summary>
    /// Defaults the supervisor can change without a deployment. Blank values are
    /// site-specific and filled in during installation.
    /// </summary>
    public static readonly (string Key, string Value)[] Settings =
    [
        // How long recordings are kept before the retention job deletes them (A-33, S-43).
        ("recording.retention_days", "90"),
        // How far back an agent's own call log reaches (A-50). A week. Not a
        // retention rule - nothing is deleted, and the contact history and the
        // supervisor's reports are unaffected.
        ("agent.call_log_days", "7"),

        // Idle logout, so a shared laptop does not leave the previous shift signed in (A-05).
        ("agent.idle_logout_minutes", "30"),

        // How long an agent may still edit their own classification (A-42).
        ("agent.edit_window", "SameDay"),

        // "Answered within N seconds" for the service level report (R-21).
        ("sla.answer_seconds", "20"),

        // Filled in at installation, once the client's PBX person has confirmed them.
        ("callback.extension", ""),

        // The PBX is the provider's Issabel, reached over the VPN, so this is its
        // address on the VPN rather than one on the restaurant LAN.
        ("pbx.host", ""),
        ("pbx.ami.enabled", "false"),

        // The other agents' and the branches' extension numbers (S-48). A call
        // whose other party is on this list is internal: still recorded, but
        // left out of the customer-facing reports. Comma separated.
        ("reports.internal_numbers", ""),
    ];

    /// <summary>The role the first account is created with.</summary>
    public const string FirstUserRole = UserRoles.Supervisor;
}
