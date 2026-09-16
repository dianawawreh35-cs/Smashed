namespace CallCenter.AgentApp.Services;

/// <summary>Where the server is, from the <c>Server</c> section of appsettings.json.</summary>
public class ServerOptions
{
    public const string SectionName = "Server";

    /// <summary>Base URL of the API, e.g. <c>http://192.168.1.10</c>.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5000";

    /// <summary>Path of the realtime hub, appended to <see cref="BaseUrl"/>.</summary>
    public string HubPath { get; set; } = "/hubs/agent";

    /// <summary>
    /// How long to wait on an API call before giving up. Short, because the app
    /// must stay usable when the server is down (A-04) rather than freeze.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
