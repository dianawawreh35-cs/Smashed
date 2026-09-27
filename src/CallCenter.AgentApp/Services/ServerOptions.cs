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

    /// <summary>
    /// How long a recording's upload may take (F-08). Its own, because the file
    /// grows about a megabyte a minute of call, and ten seconds was never
    /// enough for a long call over weak Wi-Fi: the upload failed on every
    /// retry, and until 27 Sep that stopped the whole queue behind it.
    /// </summary>
    public TimeSpan UploadTimeout { get; set; } = TimeSpan.FromMinutes(30);
}
