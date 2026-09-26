// Asks the PBX to report on agents' extensions, and prints what it says.
//
// A one-off test of whether the server can tell who is in a call without the
// Agent App (see README.md). It signs in as the server's own PBX extension (the
// one the blacklist and queue switch dial from), sends a SUBSCRIBE for the
// "dialog" event to each extension, and prints every NOTIFY that comes back.
//
//   dotnet run -c Release -- [extension ...] [--seconds N]
//
// With no extensions it watches every active agent's. Reads the dev database
// unless ConnectionStrings__Default says otherwise.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Npgsql;
using SIPSorcery.SIP;

var seconds = 180;
var trace = false;
var presence = false;
var wanted = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--seconds" && i + 1 < args.Length) seconds = int.Parse(args[++i]);
    else if (args[i] == "--trace") trace = true;
    else if (args[i] == "--presence") presence = true;
    else wanted.Add(args[i]);
}

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
    ?? "Host=127.0.0.1;Port=5432;Database=callcenter;Username=callcenter;Password=callcenter";
var sipKey = Environment.GetEnvironmentVariable("Security__SipSecretKey")
    ?? "dev-only-sip-secret-key-not-for-production";

// ---- What to sign in as, and whom to watch ------------------------------------

string? host, fromExtension, secret;
var agents = new Dictionary<string, string>();   // extension -> login

await using (var db = new NpgsqlConnection(connectionString))
{
    await db.OpenAsync();

    var settings = new Dictionary<string, string>();
    await using (var cmd = new NpgsqlCommand("select key, value from settings where key like 'pbx.%'", db))
    await using (var r = await cmd.ExecuteReaderAsync())
        while (await r.ReadAsync())
            if (!r.IsDBNull(1)) settings[r.GetString(0)] = r.GetString(1);

    host = settings.GetValueOrDefault("pbx.host");
    fromExtension = settings.GetValueOrDefault("pbx.features.extension");
    secret = Unprotect(settings.GetValueOrDefault("pbx.features.secret"), sipKey);

    await using (var cmd = new NpgsqlCommand(
        "select extension, login from users where role = 'Agent' and is_active and extension is not null order by extension", db))
    await using (var r = await cmd.ExecuteReaderAsync())
        while (await r.ReadAsync())
            agents[r.GetString(0)] = r.GetString(1);
}

if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromExtension) || string.IsNullOrEmpty(secret))
{
    Console.WriteLine("Missing: pbx.host, the server's PBX extension, or its password (Settings > PBX blacklist card).");
    return 1;
}

var targets = wanted.Count > 0 ? wanted : agents.Keys.ToList();
if (targets.Count == 0)
{
    Console.WriteLine("No active agent has an extension. Name one: dotnet run -- 105");
    return 1;
}

Console.WriteLine($"PBX {host}, signing in as extension {fromExtension}");
Console.WriteLine($"Watching {string.Join(", ", targets.Select(t => agents.TryGetValue(t, out var l) ? $"{t} ({l})" : t))} for {seconds}s");
Console.WriteLine();

// ---- SIP ----------------------------------------------------------------------

var transport = new SIPTransport();
transport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0)));

var subs = targets.ToDictionary(t => t, t => new Sub(t, CallProperties.CreateNewCallId(), CallProperties.CreateNewTag()));
var byCallId = subs.Values.ToDictionary(s => s.CallId);
var gate = new object();

if (trace)
{
    void Dump(string dir, SIPEndPoint local, SIPEndPoint remote, string msg)
    {
        lock (gate) Console.WriteLine($"---- {dir} {local} {(dir == "OUT" ? "->" : "<-")} {remote}\n{msg.TrimEnd()}\n");
    }

    transport.SIPRequestOutTraceEvent += (l, r, m) => Dump("OUT", l, r, m.ToString());
    transport.SIPResponseOutTraceEvent += (l, r, m) => Dump("OUT", l, r, m.ToString());
    transport.SIPRequestInTraceEvent += (l, r, m) => Dump("IN", l, r, m.ToString());
    transport.SIPResponseInTraceEvent += (l, r, m) => Dump("IN", l, r, m.ToString());
}

void Log(string ext, string text)
{
    var who = agents.TryGetValue(ext, out var l) ? $"{ext} ({l})" : ext;
    lock (gate) Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {who,-20} {text}");
}

transport.SIPTransportResponseReceived += async (_, _, resp) =>
{
    if (resp.Header.CSeqMethod != SIPMethodsEnum.SUBSCRIBE || !byCallId.TryGetValue(resp.Header.CallId, out var sub))
        return;

    var code = (int)resp.Status;
    if (code is 401 or 407 && sub.LastSent is { } sent && !sub.AuthTried.Contains(sent.Header.CSeq))
    {
        sub.AuthTried.Add(sent.Header.CSeq);
        var authed = sent.DuplicateAndAuthenticate(resp.Header.AuthenticationHeaders, fromExtension, secret);
        authed.Header.CSeq = ++sub.CSeq;
        authed.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
        sub.LastSent = authed;
        await transport.SendRequestAsync(authed);
        return;
    }

    if (code is >= 200 and < 300)
    {
        sub.ToTag ??= resp.Header.To.ToTag;
        if (!sub.Accepted) Log(sub.Extension, $"subscription accepted ({code} {resp.ReasonPhrase}, expires {resp.Header.Expires}s)");
        sub.Accepted = true;
    }
    else if (code >= 300)
    {
        sub.Refused = $"{code} {resp.ReasonPhrase}";
        Log(sub.Extension, $"REFUSED: {sub.Refused}{Meaning(code)}");
    }
};

transport.SIPTransportRequestReceived += async (local, remote, req) =>
{
    if (req.Method == SIPMethodsEnum.NOTIFY)
    {
        await transport.SendResponseAsync(SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.Ok, null));

        byCallId.TryGetValue(req.Header.CallId, out var sub);
        var ext = sub?.Extension ?? req.Header.From?.FromURI?.User ?? "?";
        var state = ReadState(req.Body);
        if (sub is not null)
        {
            sub.Notifies++;
            sub.LastState = state;
            if (sub.Notifies == 1) lock (gate) Console.WriteLine($"          first NOTIFY for {ext}, as received:\n{Indent(req.Body)}");
        }

        Log(ext, $"NOTIFY  {state}   (Subscription-State: {req.Header.SubscriptionState}, from {remote})");
    }
    else if (req.Method == SIPMethodsEnum.OPTIONS)
    {
        await transport.SendResponseAsync(SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.Ok, null));
    }
};

async Task SubscribeAsync(Sub sub, int expires)
{
    var uri = SIPURI.ParseSIPURI($"sip:{sub.Extension}@{host}");
    var from = new SIPFromHeader(null, SIPURI.ParseSIPURI($"sip:{fromExtension}@{host}"), sub.FromTag);
    var to = new SIPToHeader(null, uri, sub.ToTag);

    var req = SIPRequest.GetRequest(SIPMethodsEnum.SUBSCRIBE, uri, to, from);
    req.Header.CallId = sub.CallId;
    req.Header.CSeq = ++sub.CSeq;
    req.Header.Event = presence ? "presence" : "dialog";
    req.Header.Accept = presence ? "application/pidf+xml" : "application/dialog-info+xml";
    req.Header.Expires = expires;
    req.Header.Contact = [new SIPContactHeader(null, new SIPURI(fromExtension, IPAddress.Any.ToString() + ":0", null))];

    sub.LastSent = req;
    var sent = await transport.SendRequestAsync(req);
    if (sent != System.Net.Sockets.SocketError.Success) Log(sub.Extension, $"could not send the SUBSCRIBE: {sent}");
}

foreach (var sub in subs.Values) await SubscribeAsync(sub, 120);

// Renewed every 60 s, which is also what keeps the way back open.
var end = DateTime.UtcNow.AddSeconds(seconds);
var nextRenew = DateTime.UtcNow.AddSeconds(60);
while (DateTime.UtcNow < end)
{
    await Task.Delay(1000);
    if (DateTime.UtcNow >= nextRenew)
    {
        nextRenew = DateTime.UtcNow.AddSeconds(60);
        foreach (var sub in subs.Values.Where(s => s.Accepted)) await SubscribeAsync(sub, 120);
    }
}

foreach (var sub in subs.Values.Where(s => s.Accepted)) await SubscribeAsync(sub, 0);
await Task.Delay(1500);
transport.Shutdown();

Console.WriteLine();
Console.WriteLine("Summary");
foreach (var s in subs.Values)
{
    var result = s.Refused is not null ? $"refused ({s.Refused})"
        : !s.Accepted ? "no answer to the SUBSCRIBE"
        : s.Notifies == 0 ? "accepted, but NO NOTIFY arrived - the way back is blocked"
        : $"{s.Notifies} NOTIFY, last: {s.LastState}";
    Console.WriteLine($"  {s.Extension,-6} {result}");
}
return 0;

// ---- Helpers ------------------------------------------------------------------

static string ReadState(string? body)
{
    if (string.IsNullOrWhiteSpace(body)) return "(empty body)";
    try
    {
        var doc = XDocument.Parse(body);
        var basic = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "basic")?.Value;
        if (basic is not null)
        {
            var note = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "note")?.Value;
            var word = basic == "open" ? "CONNECTED" : "NOT CONNECTED";
            return note is null ? $"{word} ({basic})" : $"{word} ({basic}, note: {note})";
        }

        var dialogs = doc.Descendants().Where(e => e.Name.LocalName == "dialog").ToList();
        if (dialogs.Count == 0) return "FREE (no dialog)";

        return string.Join("; ", dialogs.Select(d =>
        {
            var state = d.Elements().FirstOrDefault(e => e.Name.LocalName == "state")?.Value ?? "?";
            var word = state switch
            {
                "early" or "proceeding" or "trying" => "RINGING",
                "confirmed" => "IN A CALL",
                "terminated" => "FREE",
                _ => state.ToUpperInvariant(),
            };
            var direction = d.Attribute("direction")?.Value;
            return direction is null ? $"{word} ({state})" : $"{word} ({state}, {direction})";
        }));
    }
    catch (Exception ex)
    {
        return $"(unreadable body: {ex.Message})";
    }
}

static string Meaning(int code) => code switch
{
    403 => " - the PBX does not let this extension subscribe",
    404 => " - no such extension, or it has no hint in the dialplan",
    489 => " - the PBX does not offer the dialog event",
    _ => "",
};

static string Indent(string? body) =>
    string.Join('\n', (body ?? "").Split('\n').Select(l => "            " + l.TrimEnd('\r')));

// The server's SipSecretProtector, read-only: v1.nonce.tag.data, base64url.
static string? Unprotect(string? ciphertext, string configuredKey)
{
    if (string.IsNullOrEmpty(ciphertext)) return null;
    var parts = ciphertext.Split('.');
    if (parts.Length != 4 || parts[0] != "v1") return null;

    byte[] key;
    try { key = Convert.FromBase64String(configuredKey); } catch { key = []; }
    if (key.Length != 32) key = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));

    static byte[] B64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    var (nonce, tag, cipher) = (B64(parts[1]), B64(parts[2]), B64(parts[3]));
    var plain = new byte[cipher.Length];
    try
    {
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
    catch (CryptographicException)
    {
        return null;
    }
}

sealed class Sub(string extension, string callId, string fromTag)
{
    public string Extension { get; } = extension;
    public string CallId { get; } = callId;
    public string FromTag { get; } = fromTag;
    public string? ToTag { get; set; }
    public int CSeq { get; set; }
    public SIPRequest? LastSent { get; set; }
    public HashSet<int> AuthTried { get; } = [];
    public bool Accepted { get; set; }
    public string? Refused { get; set; }
    public int Notifies { get; set; }
    public string? LastState { get; set; }
}
