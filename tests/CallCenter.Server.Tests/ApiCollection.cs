using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Every test class that boots the API joins this collection, so exactly one
/// host starts per test run.
/// </summary>
/// <remarks>
/// Serilog's bootstrap logger in <c>Program.cs</c> is process-wide and can only
/// be handed over to the host once; a second <c>WebApplicationFactory</c> in the
/// same process fails with "the logger is already frozen". Sharing the fixture
/// also keeps the run fast — the host is built once, not per class.
/// </remarks>
[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<CallCenterApiFactory>
{
    public const string Name = "api";
}
