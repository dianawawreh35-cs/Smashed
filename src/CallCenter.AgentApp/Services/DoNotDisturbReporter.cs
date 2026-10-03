using CallCenter.AgentApp.Services.Calls;
using CallCenter.Shared.Contracts.Auth;
using CallCenter.Shared.Contracts.Breaks;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services;

/// <summary>
/// Tells the server when do not disturb is switched (A-18), so the supervisor
/// sees it on the break monitor (S-66).
/// </summary>
/// <remarks>
/// <para>
/// Sent at sign-in, then at every switch, by hand or by Break in and Break out.
/// Only the latest state is ever sent, with the time it was switched, so a
/// resend is harmless and a late one cannot undo a newer one.
/// </para>
/// <para>
/// <b>Retried, not queued.</b> It is where the agent stands now, not a record
/// to keep: when the server cannot be reached it is tried again every
/// <see cref="RetryEvery"/> until it goes, and nothing waits in the offline
/// buffer (A-04). A server too old to know it (404) is not asked again.
/// </para>
/// </remarks>
public class DoNotDisturbReporter(
    ApiClient api,
    PhonePreferences preferences,
    ILogger<DoNotDisturbReporter> logger) : IDisposable
{
    public static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _sending = new(1, 1);

    private bool _started;
    private bool _state;
    private DateTimeOffset _at;
    private bool _pending;
    private Timer? _retry;

    /// <summary>The clock. A property so the tests can move it.</summary>
    internal Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;

    /// <summary>Starts reporting for a new sign-in, and sends the switch as it is now.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (!_started)
            {
                preferences.Changed += OnChanged;
                _started = true;
            }

            _state = preferences.DoNotDisturb;
            _at = Now();
            _pending = true;
        }

        _ = SendAsync();
    }

    /// <summary>Stops at sign-out: the server closes the sign-in, and the switch with it.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (_started)
            {
                preferences.Changed -= OnChanged;
                _started = false;
            }

            _pending = false;
            _retry?.Dispose();
            _retry = null;
        }
    }

    public void Dispose() => Stop();

    private void OnChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            // Raised for auto answer too; only a change of this switch is news.
            var now = preferences.DoNotDisturb;

            if (!_started || now == _state)
            {
                return;
            }

            _state = now;
            _at = Now();
            _pending = true;
        }

        _ = SendAsync();
    }

    /// <summary>Sends the latest state if it has not gone yet. Never throws.</summary>
    internal async Task SendAsync()
    {
        await _sending.WaitAsync();

        try
        {
            SaveDoNotDisturbRequest request;

            lock (_gate)
            {
                if (!_pending)
                {
                    return;
                }

                request = new SaveDoNotDisturbRequest(_state, _at);
            }

            ApiClient.Result<object> sent;

            try
            {
                sent = await api.SaveDoNotDisturbAsync(request);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Do not disturb could not be sent to the server");
                sent = ApiClient.Result<object>.Failed(ApiClient.ApiStatus.Unreachable, LoginErrorCodes.ServerUnreachable);
            }

            var retry = sent.Status is ApiClient.ApiStatus.Unreachable or ApiClient.ApiStatus.ServerError;

            lock (_gate)
            {
                // A switch while this was on its way is still pending, and goes next.
                if (sent.IsOk || !retry)
                {
                    if (request == new SaveDoNotDisturbRequest(_state, _at))
                    {
                        _pending = false;
                    }
                }

                if (!sent.IsOk && !retry)
                {
                    logger.LogInformation("Do not disturb not sent ({Status}, {Code}); not tried again",
                        sent.Status, sent.ErrorCode);
                }

                if (_started && _pending)
                {
                    _retry ??= new Timer(_ => _ = SendAsync(), null, RetryEvery, RetryEvery);
                }
                else
                {
                    _retry?.Dispose();
                    _retry = null;
                }
            }
        }
        finally
        {
            _sending.Release();
        }
    }
}
