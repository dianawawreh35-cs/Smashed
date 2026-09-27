namespace CallCenter.AgentApp.Services.Sip;

/// <summary>
/// Whether an incoming INVITE is for the extension this app is signed in as
/// (N-05, guard 4, 27 Sep evening).
/// </summary>
/// <remarks>
/// <para>
/// A socket can be sent another extension's calls: an agent signs out and a
/// colleague signs in on the same laptop, and the PBX still holds the first
/// extension's address, which is this same socket, until it lapses. The app
/// took those calls as its own, showed them to the wrong agent and filed them
/// under the wrong extension.
/// </para>
/// <para>
/// The registration puts the extension in its Contact
/// (<c>sendUsernameInContactHeader</c>), and Asterisk sends an INVITE to the
/// Contact it holds, so the Request-URI's user is the extension the PBX means.
/// The <c>To</c> user is the extension it dialled, which for a queue or a
/// direct dial is the same. <b>Not yet seen on a real INVITE</b>: the log
/// never carried the Request-URI until this change added it. So the rule is
/// kept lenient on purpose: a call is refused only when every user the INVITE
/// names is somebody else's, and a guess about header layout that turned out
/// wrong could not silence the phone.
/// </para>
/// </remarks>
public static class CallAddressee
{
    /// <summary>
    /// True when the call is for <paramref name="extension"/>, or when there
    /// is nothing to tell by.
    /// </summary>
    /// <param name="extension">The extension this app registered, or null before sign-in.</param>
    /// <param name="requestUriUser">The user of the INVITE's Request-URI.</param>
    /// <param name="toUser">The user of its <c>To</c> header.</param>
    public static bool IsFor(string? extension, string? requestUriUser, string? toUser)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return true;
        }

        var named = new[] { requestUriUser, toUser }
            .Where(user => !string.IsNullOrWhiteSpace(user))
            .ToList();

        return named.Count == 0
               || named.Any(user => string.Equals(user!.Trim(), extension.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The extension the INVITE names, for the log: the Request-URI's user, else the <c>To</c> user.</summary>
    public static string? Named(string? requestUriUser, string? toUser) =>
        !string.IsNullOrWhiteSpace(requestUriUser) ? requestUriUser : toUser;
}
