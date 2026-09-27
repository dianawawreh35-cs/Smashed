using System.IO;
using System.Reflection;

namespace CallCenter.AgentApp.Services;

/// <summary>
/// Identifies this laptop and this build. Laptops are shared across shifts
/// (A-05), so the server records which machine each session happened on.
/// </summary>
public static class LaptopInfo
{
    /// <summary>The Windows name of the machine, as the supervisor sees it on the network.</summary>
    public static string MachineName { get; } = Environment.MachineName;

    /// <summary>
    /// Which laptop and which install of the app: the machine name and a tag
    /// made once per install, <c>DESKTOP-RMSFSIV-7F3A2C</c>. Sent at sign-in,
    /// and on every call, message and log file.
    /// </summary>
    /// <remarks>
    /// It was the machine name alone until 27 Sep, and the three laptops, set
    /// up from one Windows image, all called themselves <c>DESKTOP-RMSFSIV</c>.
    /// The server could not tell them apart, so it could not tell "this agent
    /// signed in on another laptop" from "the same laptop signing in again"
    /// (N-05), and their logs went into one folder on the server (N-12). The
    /// tag lives in <c>%LOCALAPPDATA%\CallCenter</c>, which an update or a
    /// reinstall leaves alone. Renaming the laptops is still worth doing: the
    /// name is the part a person reads.
    /// </remarks>
    public static string LaptopId { get; } =
        Compose(MachineName, ReadOrCreateInstallId(Path.Combine(App.AppDataDirectory, InstallIdFileName)));

    /// <summary>The app version, shown in the status bar and sent with each login.</summary>
    public static string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public const string InstallIdFileName = "install-id";

    /// <summary>How many characters of the install id the laptop id carries.</summary>
    public const int TagLength = 6;

    /// <summary>The machine name and the first characters of the install id.</summary>
    public static string Compose(string machineName, Guid installId) =>
        $"{machineName}-{installId.ToString("N")[..TagLength].ToUpperInvariant()}";

    /// <summary>
    /// The id this install was given the first time it ran, made and saved now
    /// if there is none.
    /// </summary>
    /// <remarks>
    /// A file that cannot be read or written gives an id for this run only.
    /// That costs little: the server then takes the next start for another
    /// laptop, and closes a session whose app is already gone.
    /// </remarks>
    public static Guid ReadOrCreateInstallId(string path)
    {
        try
        {
            if (File.Exists(path) && Guid.TryParse(File.ReadAllText(path).Trim(), out var saved))
            {
                return saved;
            }

            var made = Guid.NewGuid();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, made.ToString("D"));
            return made;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Guid.NewGuid();
        }
    }
}
