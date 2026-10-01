namespace CallCenter.AgentApp.Services.Sip;

/// <summary>
/// When to log in again after the PBX refused the password of an extension
/// that has already registered on this sign-in (A-02).
/// </summary>
/// <remarks>
/// The password is known to be right, so the refusal is the PBX asking again:
/// Issabel does that when the answer to its challenge arrives after the
/// one-time code in it has expired, as it did after a 30 s dropout on 1 Oct.
/// The app keeps trying for the rest of the sign-in, but slows down: if the
/// password really was changed at the PBX, every try is a failed login there,
/// and fail2ban blocks a laptop that fails too often.
/// </remarks>
internal static class ReRegistration
{
    /// <summary>The waits before the first tries; every later one waits the last.</summary>
    private static readonly TimeSpan[] Waits =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
    ];

    /// <summary>
    /// How many refusals in a row, a few minutes apart, before the agent is told
    /// to ask their supervisor rather than wait.
    /// </summary>
    private const int ShownAsWrongAfter = 3;

    /// <summary>How long to wait after this many refusals in a row (1 or more).</summary>
    public static TimeSpan Wait(int refusalsInARow) =>
        Waits[Math.Clamp(refusalsInARow, 1, Waits.Length) - 1];

    /// <summary>
    /// True once the refusals have gone on long enough that the password was
    /// probably changed at the PBX. The app still tries again.
    /// </summary>
    public static bool LooksWrong(int refusalsInARow) => refusalsInARow >= ShownAsWrongAfter;
}
