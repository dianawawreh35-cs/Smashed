using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// A test that needs PostgreSQL. Runs when the run was given a database (CI, or
/// a developer who set <c>ConnectionStrings__Default</c>); otherwise it is
/// reported as <b>skipped</b>, never as passed.
/// </summary>
/// <remarks>
/// Skipped, not silently returned from, because a test that passes without
/// doing anything is how 176 green tests once sat beside a login that answered
/// 500 to everybody.
/// </remarks>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (!CallCenterApiFactory.HasDatabase)
        {
            Skip = "Needs PostgreSQL: set ConnectionStrings__Default to run it.";
        }
    }
}

/// <summary>The <see cref="DatabaseFactAttribute"/> rule, for a test with cases.</summary>
public sealed class DatabaseTheoryAttribute : TheoryAttribute
{
    public DatabaseTheoryAttribute()
    {
        if (!CallCenterApiFactory.HasDatabase)
        {
            Skip = "Needs PostgreSQL: set ConnectionStrings__Default to run it.";
        }
    }
}

/// <summary>
/// A test that only means something in the restaurant's time zone, where the
/// clocks change on known days (F-06). CI runs in <c>TZ=Asia/Hebron</c>, as the
/// production container does; a laptop set to "West Bank Standard Time" is the
/// same zone. Anywhere else it is reported as skipped.
/// </summary>
public sealed class HebronFactAttribute : FactAttribute
{
    public HebronFactAttribute(bool needsDatabase = false)
    {
        if (!IsHebron)
        {
            Skip = $"Needs the restaurant's time zone (TZ=Asia/Hebron); this run is in {TimeZoneInfo.Local.Id}.";
        }
        else if (needsDatabase && !CallCenterApiFactory.HasDatabase)
        {
            Skip = "Needs PostgreSQL: set ConnectionStrings__Default to run it.";
        }
    }

    public static bool IsHebron => TimeZoneInfo.Local.Id is "Asia/Hebron" or "West Bank Standard Time";
}
