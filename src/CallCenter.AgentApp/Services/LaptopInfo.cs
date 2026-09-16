using System.Reflection;

namespace CallCenter.AgentApp.Services;

/// <summary>
/// Identifies this laptop and this build. Laptops are shared across shifts
/// (A-05), so the server records which machine each session happened on.
/// </summary>
public static class LaptopInfo
{
    /// <summary>The machine name, as the supervisor sees it on the network.</summary>
    public static string LaptopId { get; } = Environment.MachineName;

    /// <summary>The app version, shown in the status bar and sent with each login.</summary>
    public static string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
}
