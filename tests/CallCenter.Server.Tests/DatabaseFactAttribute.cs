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
