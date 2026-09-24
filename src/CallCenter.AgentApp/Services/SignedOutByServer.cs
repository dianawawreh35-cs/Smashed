using CallCenter.AgentApp.Services.Calls;
using CallCenter.Shared.Contracts.Auth;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services;

/// <summary>
/// Signs the agent out when the server stops accepting their sign-in (N-05):
/// a supervisor reset the password, disabled the account or changed its role,
/// or the token simply ran out.
/// </summary>
/// <remarks>
/// <para>
/// Without this the app carried on as if nothing had happened. Every lookup
/// failed, the call queue grew, and the phone stayed registered, so an account
/// a supervisor had just disabled went on taking calls. Now the first 401 to a
/// signed-in request unregisters the phone and goes back to the sign-in screen,
/// with a line saying why.
/// </para>
/// <para>
/// <b>Never in the middle of a call.</b> If the agent is talking to a customer,
/// the sign-out waits until the call ends. Hanging up on a customer because of
/// an account change they know nothing about would be worse than a few more
/// minutes on an account that is going anyway. Nothing that happens during
/// those minutes is lost: the call queue keeps what the server refused and
/// sends it after the next sign-in.
/// </para>
/// </remarks>
public class SignedOutByServer
{
    private readonly AgentSession _session;
    private readonly CallService _calls;
    private readonly SignInService _signIn;
    private readonly ILogger<SignedOutByServer> _logger;

    // 0 = nothing to do, 1 = refused and waiting, 2 = signing out now.
    private int _state;

    public SignedOutByServer(
        AgentSession session, CallService calls, SignInService signIn, ILogger<SignedOutByServer> logger)
    {
        _session = session;
        _calls = calls;
        _signIn = signIn;
        _logger = logger;

        session.TokenRefused += (_, _) => OnTokenRefused();
        calls.StateChanged += (_, state) => OnCallState(state);
    }

    /// <summary>
    /// Raised once the agent has been signed out, off the UI thread. The window
    /// goes back to the sign-in screen with <see cref="LoginErrorCodes.SignedOut"/>.
    /// </summary>
    public event EventHandler? SignedOut;

    private void OnTokenRefused()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            // Already waiting or already signing out. A burst of refused
            // requests, or the logout call itself being refused, lands here.
            return;
        }

        if (_calls.State.Status == CallStatus.Idle)
        {
            _ = SignOutAsync();
        }
        else
        {
            _logger.LogWarning("The server no longer accepts this sign-in; signing out when the call ends.");
        }
    }

    private void OnCallState(CallState state)
    {
        if (state.Status == CallStatus.Idle && Volatile.Read(ref _state) == 1)
        {
            _ = SignOutAsync();
        }
    }

    private async Task SignOutAsync()
    {
        if (Interlocked.CompareExchange(ref _state, 2, 1) != 1)
        {
            return;
        }

        try
        {
            _logger.LogWarning("The server no longer accepts this sign-in; signing out.");

            // Unregisters the phone first, as a manual sign-out does. The logout
            // call to the server will be refused too, which is expected and
            // harmless: the server already treats the token as dead.
            await _signIn.SignOutAsync(LogoutReasons.Forced);

            SignedOut?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Signing out after the server refused the sign-in failed");
        }
        finally
        {
            Volatile.Write(ref _state, 0);
        }
    }
}
