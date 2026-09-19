using System.IO;
using System.Text.Json;
using CallCenter.Shared.Contracts.Communications;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services.Calls;

/// <summary>
/// Calls waiting to reach the server (A-14, A-04).
/// </summary>
/// <remarks>
/// A-04 requires the app to keep working when the server is unreachable, and a
/// call log that quietly loses a shift's calls whenever the network blinks would
/// be worse than no call log — the supervisor's reports would be wrong without
/// anybody knowing they were wrong.
///
/// So every call is written to disk first and only removed once the server has
/// acknowledged it. The queue survives a crash, a reboot and a flat battery.
///
/// <b>Deliberately not SQLite.</b> The offline buffer in the plan is an EF Core
/// SQLite database, whose native library is still pinned to a version carrying
/// CVE-2025-6965. This queue needs to append a record, read them back in order
/// and delete the ones that succeeded; a JSON-per-line file does that in fifty
/// lines and pulls in nothing. If the buffer later grows to hold recordings and
/// classifications, that is the point to revisit it — and to pin the library
/// first.
///
/// Resending is safe because the server keys a call on its SIP Call-ID and
/// extension: a call sent twice updates rather than duplicates, so this never
/// has to work out what already arrived.
/// </remarks>
public class CallLogQueue(ILogger<CallLogQueue> logger)
{
    /// <summary>
    /// How many queued calls are kept. A laptop offline for a week should not
    /// fill its disk; the oldest go first, because a recent call is the one
    /// somebody is still asking about.
    /// </summary>
    public const int MaxQueued = 5000;

    private readonly string _path = Path.Combine(App.AppDataDirectory, "pending-calls.jsonl");
    private readonly Lock _gate = new();

    /// <summary>Adds a call to the queue. Called before the send is attempted.</summary>
    public void Enqueue(LogCallRequest call)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(App.AppDataDirectory);

                // One JSON object per line, appended. Append rather than rewrite
                // so a power cut during a write can cost at most the call being
                // written, never the ones already queued.
                File.AppendAllText(_path, JsonSerializer.Serialize(call) + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nothing else to do: the call is about to be sent anyway, and
                // failing here must not interrupt the agent.
                logger.LogWarning(ex, "A call could not be queued to {Path}", _path);
            }
        }
    }

    /// <summary>Everything still waiting, oldest first.</summary>
    public IReadOnlyList<LogCallRequest> Pending()
    {
        lock (_gate)
        {
            return Read();
        }
    }

    /// <summary>
    /// Removes calls the server has accepted, identified by Call-ID and
    /// extension — the same pair the server keys on.
    /// </summary>
    public void Acknowledge(IEnumerable<LogCallRequest> sent)
    {
        var done = sent.Select(Key).ToHashSet();

        lock (_gate)
        {
            var remaining = Read().Where(c => !done.Contains(Key(c))).ToList();
            Write(remaining);
        }
    }

    private List<LogCallRequest> Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            var calls = new List<LogCallRequest>();

            foreach (var line in File.ReadAllLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    if (JsonSerializer.Deserialize<LogCallRequest>(line) is { } call)
                    {
                        calls.Add(call);
                    }
                }
                catch (JsonException)
                {
                    // One torn line — the usual cause is a power cut mid-append.
                    // Skip it and keep the rest, rather than losing the file.
                    logger.LogWarning("A queued call could not be read and was skipped");
                }
            }

            return calls;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The queued calls at {Path} could not be read", _path);
            return [];
        }
    }

    private void Write(List<LogCallRequest> calls)
    {
        try
        {
            if (calls.Count == 0)
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }

                return;
            }

            if (calls.Count > MaxQueued)
            {
                var dropped = calls.Count - MaxQueued;
                calls = calls.Skip(dropped).ToList();

                logger.LogError(
                    "The call queue exceeded {Max}; {Dropped} of the oldest call(s) were discarded",
                    MaxQueued, dropped);
            }

            File.WriteAllLines(_path, calls.Select(c => JsonSerializer.Serialize(c)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The calls stay queued and are retried later, which is the safe
            // failure: a duplicate send is harmless, a lost call is not.
            logger.LogWarning(ex, "The call queue could not be written to {Path}", _path);
        }
    }

    private static string Key(LogCallRequest call) => $"{call.SipCallId}|{call.Extension}";
}
