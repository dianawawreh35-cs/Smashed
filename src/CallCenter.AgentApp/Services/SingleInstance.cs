using System.Threading;

namespace CallCenter.AgentApp.Services;

/// <summary>
/// One copy of the Agent App per Windows sign-in (N-05, 27 Sep evening). A
/// second launch brings the first copy's window forward and exits.
/// </summary>
/// <remarks>
/// <para>
/// On 27 Sep calls were missed because a second copy was signed in as the same
/// agent: the extension's registration at the PBX followed whichever copy
/// refreshed last, so the rings went to a window nobody was looking at, and a
/// copy with Do Not Disturb off showed pop-ups the agent's own copy would have
/// refused. Nothing stopped a second copy being started.
/// </para>
/// <para>
/// <b>Held by existing, not by owning.</b> The first copy creates a named
/// mutex and keeps the handle open until it exits; it never waits on it. A
/// second copy creating the same name is told it already existed. So there is
/// nothing to abandon: when a copy crashes or is ended in Task Manager, Windows
/// closes its handles, the name goes, and the next start is the first again.
/// Owning it instead would tie it to the UI thread (a mutex is released by the
/// thread that took it), across an <c>async void</c> start-up and a shutdown
/// that pumps the dispatcher, for no gain.
/// </para>
/// <para>
/// <b>Per Windows sign-in</b> (<c>Local\</c>), not per machine: two people
/// signed in to Windows on one laptop each have their own
/// <c>%LOCALAPPDATA%</c>, and the server's one-sign-in rule settles the rest.
/// </para>
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    public const string DefaultName = "Smashed.CallCenter.AgentApp";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showRequests;
    private RegisteredWaitHandle? _listening;

    private SingleInstance(Mutex mutex, EventWaitHandle showRequests)
    {
        _mutex = mutex;
        _showRequests = showRequests;
    }

    /// <summary>
    /// This copy, when it is the only one; otherwise null, after asking the
    /// copy already running to come forward.
    /// </summary>
    /// <param name="name">The name both handles are made from; a test uses its own.</param>
    public static SingleInstance? Claim(string name = DefaultName)
    {
        var mutex = new Mutex(initiallyOwned: false, $@"Local\{name}", out var createdNew);

        if (!createdNew)
        {
            mutex.Dispose();
            AskTheFirstCopyToShow(name);
            return null;
        }

        var showRequests = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName(name));
        return new SingleInstance(mutex, showRequests);
    }

    /// <summary>
    /// Runs <paramref name="show"/>, on a pool thread, each time another copy is
    /// started and asks this one to come forward.
    /// </summary>
    public void OnShowRequested(Action show)
    {
        _listening?.Unregister(null);
        _listening = ThreadPool.RegisterWaitForSingleObject(
            _showRequests, (_, _) => show(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    private static string ShowEventName(string name) => $@"Local\{name}.Show";

    /// <summary>
    /// A copy that is still starting may not have made its event yet. Then
    /// nothing comes forward, and the agent sees the window a moment later
    /// anyway, when that copy finishes starting.
    /// </summary>
    private static void AskTheFirstCopyToShow(string name)
    {
        if (EventWaitHandle.TryOpenExisting(ShowEventName(name), out var first))
        {
            using (first)
            {
                // The agent just started this copy, so it may bring a window
                // to the front; this hands that right to the first copy.
                // Without it Windows only flashes the first copy's taskbar
                // button.
                AllowSetForegroundWindow(AnyProcess);
                first.Set();
            }
        }
    }

    private const int AnyProcess = -1;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>Lets the next start be the first. Called at the very end of shutdown.</summary>
    public void Dispose()
    {
        _listening?.Unregister(null);
        _listening = null;
        _showRequests.Dispose();
        _mutex.Dispose();
    }
}
