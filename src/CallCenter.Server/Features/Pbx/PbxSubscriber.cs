using System.Net;
using SIPSorcery.SIP;

namespace CallCenter.Server.Features.Pbx;

/// <summary>
/// Keeps the PBX telling the server about each agent's extension (S-61): a
/// SUBSCRIBE for the <c>presence</c> event and one for <c>dialog</c>, renewed
/// every minute, with every NOTIFY passed to <see cref="ExtensionWatch"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a desk phone's busy lamp does.</b> The server asks as its own
/// extension, the one S-46 and S-60 dial from, with that extension's login.
/// Tried against the PBX on 26 Sep with <c>tools/presence-probe</c>: both
/// events accepted, the state sent at once, and the change sent within a
/// second of a call being answered and hung up.
/// </para>
/// <para>
/// <b>Outbound, like everything else here</b> (SRS 4.5). The server opens the
/// subscription from its own port, and the PBX's NOTIFYs come back to that
/// port. Renewing every minute is also what keeps that way back open through a
/// NAT.
/// </para>
/// <para>
/// <b>Anything that goes wrong starts that subscription again</b>, from
/// scratch, on the next <see cref="Tick"/>: a refusal, a subscription the PBX
/// ended, one it no longer knows (481), or a SUBSCRIBE that got no answer.
/// UDP loses the odd packet, and a fresh subscription is simpler than
/// retransmitting.
/// </para>
/// </remarks>
public sealed class PbxSubscriber : IDisposable
{
    /// <summary>What the server asks for. The PBX may grant another; half of what it grants is when to renew.</summary>
    public const int Expires = 120;

    /// <summary>A SUBSCRIBE with no answer after this long is sent again from scratch.</summary>
    public static readonly TimeSpan NoAnswerAfter = TimeSpan.FromSeconds(20);

    /// <summary>
    /// An extension the PBX refused (404 for one it does not have, 403) is
    /// asked about again only this often, not every tick.
    /// </summary>
    public static readonly TimeSpan RetryRefusedAfter = TimeSpan.FromMinutes(5);

    private readonly PbxExtension _line;
    private readonly ExtensionWatch _watch;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly SIPTransport _transport = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Extension, bool Presence), Sub> _subs = [];
    private readonly Dictionary<string, Sub> _byCallId = new(StringComparer.Ordinal);

    public PbxSubscriber(PbxExtension line, ExtensionWatch watch, TimeProvider clock, ILogger logger)
    {
        _line = line;
        _watch = watch;
        _clock = clock;
        _logger = logger;

        _transport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0)));
        _transport.SIPTransportResponseReceived += OnResponseAsync;
        _transport.SIPTransportRequestReceived += OnRequestAsync;
    }

    /// <summary>Which PBX and login this subscriber was made for.</summary>
    public PbxExtension Line => _line;

    private enum SubState
    {
        /// <summary>Sent, not yet answered.</summary>
        Pending,
        Active,
        /// <summary>To be started again from scratch.</summary>
        Dead,
    }

    private sealed class Sub(string extension, bool presence)
    {
        public string Extension { get; } = extension;
        public bool Presence { get; } = presence;
        public string CallId { get; set; } = CallProperties.CreateNewCallId();
        public string FromTag { get; set; } = CallProperties.CreateNewTag();
        public string? ToTag { get; set; }
        public int CSeq { get; set; }
        public SIPRequest? LastSent { get; set; }
        public HashSet<int> AuthTried { get; } = [];
        public SubState State { get; set; } = SubState.Dead;
        public DateTimeOffset SentAt { get; set; }
        public TimeSpan RenewEvery { get; set; } = TimeSpan.FromSeconds(Expires / 2);
        public string? Refused { get; set; }

        /// <summary>Not before this, after a refusal.</summary>
        public DateTimeOffset RetryAt { get; set; }

        public string Event => Presence ? "presence" : "dialog";
    }

    /// <summary>
    /// Brings the subscriptions into line with <paramref name="extensions"/>:
    /// starts new ones, renews those that are due, starts again any that failed,
    /// and ends those for extensions no agent has any more.
    /// </summary>
    public async Task Tick(IReadOnlyCollection<string> extensions)
    {
        var now = _clock.GetUtcNow();
        var toSend = new List<(Sub Sub, int Expires, bool Fresh)>();

        lock (_gate)
        {
            foreach (var extension in extensions)
            {
                foreach (var presence in new[] { true, false })
                {
                    if (!_subs.ContainsKey((extension, presence)))
                    {
                        _subs[(extension, presence)] = new Sub(extension, presence);
                    }
                }
            }

            foreach (var (key, sub) in _subs.ToList())
            {
                if (!extensions.Contains(key.Extension))
                {
                    _subs.Remove(key);
                    _byCallId.Remove(sub.CallId);
                    if (sub.State == SubState.Active)
                    {
                        toSend.Add((sub, 0, false));
                    }

                    continue;
                }

                switch (sub.State)
                {
                    case SubState.Dead when now < sub.RetryAt:
                        break;

                    case SubState.Dead:
                    case SubState.Pending when now - sub.SentAt > NoAnswerAfter:
                        toSend.Add((sub, Expires, true));
                        break;

                    case SubState.Active when now - sub.SentAt >= sub.RenewEvery:
                        toSend.Add((sub, Expires, false));
                        break;
                }
            }
        }

        foreach (var (sub, expires, fresh) in toSend)
        {
            await SubscribeAsync(sub, expires, fresh);
        }
    }

    private async Task SubscribeAsync(Sub sub, int expires, bool fresh)
    {
        SIPRequest request;

        lock (_gate)
        {
            if (fresh)
            {
                _byCallId.Remove(sub.CallId);
                sub.CallId = CallProperties.CreateNewCallId();
                sub.FromTag = CallProperties.CreateNewTag();
                sub.ToTag = null;
                sub.AuthTried.Clear();
            }

            if (expires > 0)
            {
                _byCallId[sub.CallId] = sub;
            }

            var uri = SIPURI.ParseSIPURI($"sip:{sub.Extension}@{_line.Host}");
            var from = new SIPFromHeader(null, SIPURI.ParseSIPURI($"sip:{_line.Extension}@{_line.Host}"), sub.FromTag);
            var to = new SIPToHeader(null, uri, sub.ToTag);

            request = SIPRequest.GetRequest(SIPMethodsEnum.SUBSCRIBE, uri, to, from);
            request.Header.CallId = sub.CallId;
            request.Header.CSeq = ++sub.CSeq;
            request.Header.Event = sub.Event;
            request.Header.Accept = sub.Presence ? "application/pidf+xml" : "application/dialog-info+xml";
            request.Header.Expires = expires;

            // 0.0.0.0:0 is filled in with the address and port the request
            // leaves from, which is where the PBX sends its NOTIFYs.
            request.Header.Contact = [new SIPContactHeader(null, new SIPURI(_line.Extension, "0.0.0.0:0", null))];

            sub.LastSent = request;
            sub.SentAt = _clock.GetUtcNow();
            if (expires > 0 && sub.State != SubState.Active)
            {
                sub.State = SubState.Pending;
            }
        }

        try
        {
            await _transport.SendRequestAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PBX watch: the SUBSCRIBE for {Extension} ({Event}) could not be sent", sub.Extension, sub.Event);
        }
    }

    private async Task OnResponseAsync(SIPEndPoint local, SIPEndPoint remote, SIPResponse response)
    {
        if (response.Header.CSeqMethod != SIPMethodsEnum.SUBSCRIBE)
        {
            return;
        }

        Sub? sub;
        SIPRequest? retry = null;
        var code = (int)response.Status;

        lock (_gate)
        {
            if (!_byCallId.TryGetValue(response.Header.CallId, out sub) || sub.LastSent is not { } sent
                || response.Header.CSeq != sent.Header.CSeq)
            {
                return;
            }

            if (code is 401 or 407)
            {
                if (!sub.AuthTried.Add(sent.Header.CSeq))
                {
                    sub.State = SubState.Dead;
                    sub.RetryAt = _clock.GetUtcNow() + RetryRefusedAfter;
                    Refuse(sub, $"{code} {response.ReasonPhrase}: the PBX did not accept extension {_line.Extension}'s password");
                    return;
                }

                retry = sent.DuplicateAndAuthenticate(response.Header.AuthenticationHeaders, _line.Extension, _line.Secret);
                retry.Header.CSeq = ++sub.CSeq;
                retry.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
                sub.LastSent = retry;
            }
            else if (code is >= 200 and < 300)
            {
                sub.ToTag ??= response.Header.To.ToTag;
                sub.State = SubState.Active;
                if (response.Header.Expires > 0)
                {
                    sub.RenewEvery = TimeSpan.FromSeconds(Math.Max(10, response.Header.Expires / 2));
                }

                if (sub.Refused is not null)
                {
                    _logger.LogInformation("PBX watch: {Extension} ({Event}) is accepted again", sub.Extension, sub.Event);
                    sub.Refused = null;
                }
            }
            else if (code >= 300)
            {
                sub.State = SubState.Dead;

                // 481 is a renewal of a subscription the PBX has already
                // dropped: start again at once. Anything else is the PBX
                // saying no, and asking every tick would not change its mind.
                if (code != 481)
                {
                    sub.RetryAt = _clock.GetUtcNow() + RetryRefusedAfter;
                    Refuse(sub, $"{code} {response.ReasonPhrase}");
                }
            }
        }

        if (retry is not null)
        {
            await _transport.SendRequestAsync(retry);
        }
        else if (code is >= 200 and < 300)
        {
            _watch.Heard();
            _watch.Confirmed(sub.Extension, sub.Presence);
        }
    }

    /// <summary>Logged once per change, not once a minute.</summary>
    private void Refuse(Sub sub, string why)
    {
        if (sub.Refused != why)
        {
            sub.Refused = why;
            _logger.LogWarning("PBX watch: the PBX refused to report on {Extension} ({Event}): {Why}",
                sub.Extension, sub.Event, why);
        }
    }

    private async Task OnRequestAsync(SIPEndPoint local, SIPEndPoint remote, SIPRequest request)
    {
        if (request.Method == SIPMethodsEnum.OPTIONS)
        {
            await _transport.SendResponseAsync(SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.Ok, null));
            return;
        }

        if (request.Method != SIPMethodsEnum.NOTIFY)
        {
            return;
        }

        Sub? sub;
        lock (_gate)
        {
            _byCallId.TryGetValue(request.Header.CallId, out sub);
            if (sub is not null && request.Header.SubscriptionState?.StartsWith("terminated", StringComparison.OrdinalIgnoreCase) == true)
            {
                // The PBX ended it: start again on the next tick.
                sub.State = SubState.Dead;
            }
        }

        // A NOTIFY for a subscription this server no longer has is told so,
        // and the PBX stops sending them.
        await _transport.SendResponseAsync(SIPResponse.GetResponse(request,
            sub is null ? SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist : SIPResponseStatusCodesEnum.Ok, null));

        if (sub is null)
        {
            return;
        }

        string? changed = null;
        if (sub.Presence)
        {
            if (PbxNotify.ReadPresence(request.Body) is { } connected)
            {
                changed = _watch.Presence(sub.Extension, connected);
            }
        }
        else if (PbxNotify.ReadDialog(request.Body) is { } dialog)
        {
            changed = _watch.Dialog(sub.Extension, dialog);
        }

        _watch.Heard();

        // One line per change, so the log answers "was 2001 on a call at 14:05".
        if (changed is not null)
        {
            _logger.LogInformation("PBX watch: {Extension} is now {State}", sub.Extension, changed);
        }
    }

    /// <summary>Ends every subscription, without waiting for the PBX to agree, and closes the port.</summary>
    public void Dispose()
    {
        List<Sub> active;
        lock (_gate)
        {
            active = _subs.Values.Where(s => s.State == SubState.Active).ToList();
            _subs.Clear();
            _byCallId.Clear();
        }

        try
        {
            Task.WhenAll(active.Select(s => SubscribeAsync(s, 0, fresh: false))).Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PBX watch: ending the subscriptions did not go cleanly");
        }

        _transport.Shutdown();
    }
}
