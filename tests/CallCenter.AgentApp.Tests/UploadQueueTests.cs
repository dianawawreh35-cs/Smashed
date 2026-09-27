using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using CallCenter.AgentApp.Data;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Calls;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Auth;
using CallCenter.Shared.Contracts.Classifications;
using CallCenter.Shared.Contracts.Communications;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CallCenter.AgentApp.Tests;

/// <summary>
/// The offline buffer and the pass that empties it (A-04, F-03, F-08, M-A03),
/// against a real SQLite file of the test's own and a stubbed server.
/// </summary>
public sealed class UploadQueueTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"cc-queue-{Guid.NewGuid():N}");
    private readonly AgentSession _session = new();
    private readonly Buffer _buffer;

    private static readonly Guid AgentA = Guid.NewGuid();
    private static readonly Guid AgentB = Guid.NewGuid();

    public UploadQueueTests()
    {
        Directory.CreateDirectory(_folder);
        _buffer = new Buffer(Path.Combine(_folder, "agent-buffer.db"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Windows may still hold the file for a moment; it is in Temp.
        }
    }

    // ---- F-03: whose rows these are ------------------------------------------

    [Fact]
    public async Task Rows_are_sent_only_as_the_agent_who_queued_them()
    {
        var server = new FakeServer();
        var (queue, reporter) = await Build(server);

        SignIn(AgentA, "2001");
        await queue.EnqueueAsync(Call("a-1", "2001"));

        // Agent A signs out with the server down; agent B signs in.
        SignIn(AgentB, "2002");
        await reporter.FlushAsync();

        server.Logged.Should().BeEmpty("agent A's call must not be filed as agent B's");

        SignIn(AgentA, "2001");
        await reporter.FlushAsync();

        server.Logged.Should().Equal("a-1");
    }

    [Fact]
    public async Task Rows_from_before_owners_were_recorded_go_once_as_the_next_agent()
    {
        var server = new FakeServer();
        var (queue, reporter) = await Build(server);

        // What an older version wrote: a row with no owner.
        await using (var db = _buffer.CreateDbContext())
        {
            db.PendingUploads.Add(Row(Call("old-1", "2001"), userId: null));
            await db.SaveChangesAsync();
        }

        SignIn(AgentB, "2002");
        await reporter.FlushAsync();

        server.Logged.Should().Equal("old-1");

        await using var check = _buffer.CreateDbContext();
        (await check.PendingUploads.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_buffer_made_before_the_owner_column_is_upgraded_and_keeps_its_rows()
    {
        // The first version's table, exactly: no UserId, no SetAsideAt.
        await using (var connection = new SqliteConnection(_buffer.ConnectionString))
        {
            await connection.OpenAsync();
            var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE "pending_uploads" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_pending_uploads" PRIMARY KEY AUTOINCREMENT,
                    "Kind" TEXT NOT NULL, "Payload" TEXT NOT NULL, "Reference" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL, "Attempts" INTEGER NOT NULL, "LastError" TEXT NULL);
                INSERT INTO pending_uploads (Kind, Payload, Reference, CreatedAt, Attempts)
                VALUES ('Call', $payload, 'kept|2001', '2026-09-26 18:00:00+03:00', 0);
                """;
            create.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(Call("kept", "2001")));
            await create.ExecuteNonQueryAsync();
        }

        var server = new FakeServer();
        var (_, reporter) = await Build(server);

        SignIn(AgentA, "2001");
        await reporter.FlushAsync();

        server.Logged.Should().Equal("kept");
    }

    // ---- F-08: nothing stops the queue for good -----------------------------------

    [Fact]
    public async Task A_call_the_server_keeps_failing_is_set_aside_and_the_rest_go()
    {
        var server = new FakeServer { FailCall = { ["bad"] = HttpStatusCode.InternalServerError } };
        var (queue, reporter) = await Build(server);
        SignIn(AgentA, "2001");

        await queue.EnqueueAsync(Call("bad", "2001"));

        for (var i = 0; i < CallLogReporter.MaxAttempts; i++)
        {
            // Each pass has something after the bad call that goes through,
            // which is what makes its failure count.
            await queue.EnqueueAsync(Call($"good-{i}", "2001"));
            await reporter.FlushAsync();
        }

        server.Logged.Should().HaveCount(CallLogReporter.MaxAttempts).And.NotContain("bad");
        reporter.SetAsideCount.Should().Be(1);

        var aside = await reporter.SetAsideItemsAsync();
        aside.Should().ContainSingle().Which.Reason.Should().Be("gave_up");
    }

    [Fact]
    public async Task A_server_that_is_down_wears_nothing_out()
    {
        var server = new FakeServer { Down = true };
        var (queue, reporter) = await Build(server);
        SignIn(AgentA, "2001");

        await queue.EnqueueAsync(Call("waiting", "2001"));

        for (var i = 0; i < CallLogReporter.MaxAttempts * 3; i++)
        {
            await reporter.FlushAsync();
        }

        reporter.SetAsideCount.Should().Be(0);

        server.Down = false;
        await reporter.FlushAsync();

        server.Logged.Should().Equal("waiting");
    }

    [Fact]
    public async Task A_refusal_with_no_code_is_permanent_and_takes_its_classification_with_it()
    {
        // A 400 from model validation carries no "code".
        var server = new FakeServer { FailCall = { ["refused"] = HttpStatusCode.BadRequest } };
        var (queue, reporter) = await Build(server);
        SignIn(AgentA, "2001");

        await queue.EnqueueAsync(Classify("refused", "2001"));
        await queue.EnqueueAsync(Call("refused", "2001"));
        await queue.EnqueueAsync(Call("after", "2001"));

        await reporter.FlushAsync();

        server.Logged.Should().Equal("after");
        server.Classified.Should().BeEmpty();
        reporter.SetAsideCount.Should().Be(2, "the call, and the classification that can never find it");
    }

    [Fact]
    public async Task Extension_not_yours_is_never_retried()
    {
        var server = new FakeServer { NotYours = { "stale" } };
        var (queue, reporter) = await Build(server);
        SignIn(AgentA, "2001");

        await queue.EnqueueAsync(Call("stale", "2001"));
        await reporter.FlushAsync();
        await reporter.FlushAsync();

        server.Attempts("stale").Should().Be(1);
        (await reporter.SetAsideItemsAsync()).Should().ContainSingle()
            .Which.Reason.Should().Be("extension_not_yours");
    }

    [Fact]
    public async Task A_classification_saved_during_the_call_still_goes_after_it()
    {
        var server = new FakeServer();
        var (queue, reporter) = await Build(server);
        SignIn(AgentA, "2001");

        // Saved while talking: queued, and refused as "call_not_found" until
        // the call has been reported.
        await queue.EnqueueAsync(Classify("c-1", "2001"));
        await reporter.FlushAsync();

        server.Classified.Should().BeEmpty();
        reporter.SetAsideCount.Should().Be(0);

        await queue.EnqueueAsync(Call("c-1", "2001"));
        await reporter.FlushAsync();

        server.Logged.Should().Equal("c-1");
        server.Classified.Should().Equal("c-1");
    }

    [Fact]
    public async Task Put_back_on_the_queue_it_is_sent_once_the_server_takes_it()
    {
        var server = new FakeServer { FailCall = { ["later"] = HttpStatusCode.BadRequest } };
        var (queue, reporter) = await Build(server);
        SignIn(AgentA, "2001");

        await queue.EnqueueAsync(Call("later", "2001"));
        await reporter.FlushAsync();
        reporter.SetAsideCount.Should().Be(1);

        server.FailCall.Clear();
        await reporter.RetrySetAsideAsync();

        server.Logged.Should().Equal("later");
        reporter.SetAsideCount.Should().Be(0);
    }

    [Fact]
    public void A_name_longer_than_the_server_takes_is_cut_before_it_is_queued()
    {
        var call = new FinishedCall(
            "long", new string('9', 60), new string('x', 500), new string('q', 300),
            CallOutcome.Answered, DateTimeOffset.Now, DateTimeOffset.Now, DateTimeOffset.Now);

        var request = CallLogReporter.Describe(call, "2001");

        request.RemoteNumber!.Length.Should().Be(40);
        request.RemoteName!.Length.Should().Be(200);
        request.Queue!.Length.Should().Be(100);
        request.SipCallId.Should().Be("long", "the key is never shortened");
    }

    // ---- M-A03: a save that could not be kept says so ------------------------------------

    [Fact]
    public async Task A_classification_the_buffer_cannot_take_is_sent_directly_or_reported_failed()
    {
        var server = new FakeServer();
        var broken = new CallLogQueue(new BrokenBuffer(), _session, NullLogger<CallLogQueue>.Instance) { LegacyFile = null };
        var reporter = new CallLogReporter(
            server.Client(_session), _session, broken, new AgentNotices(), NullLogger<CallLogReporter>.Instance);
        SignIn(AgentA, "2001");

        // The call is on the server, so the direct send works.
        server.Known.Add("known");
        (await reporter.ClassifyAsync(Classify("known", "2001"))).Should().Be(CallLogReporter.SaveOutcome.Sent);

        // The call is not, as during a call: nothing kept it.
        (await reporter.ClassifyAsync(Classify("unknown", "2001"))).Should().Be(CallLogReporter.SaveOutcome.Failed);
    }

    // ---- helpers ----------------------------------------------------------------

    private async Task<(CallLogQueue Queue, CallLogReporter Reporter)> Build(FakeServer server)
    {
        var queue = new CallLogQueue(_buffer, _session, NullLogger<CallLogQueue>.Instance) { LegacyFile = null };
        await queue.InitialiseAsync();

        var reporter = new CallLogReporter(
            server.Client(_session), _session, queue, new AgentNotices(), NullLogger<CallLogReporter>.Instance);

        return (queue, reporter);
    }

    private void SignIn(Guid userId, string extension) =>
        _session.SignIn(new LoginResponse(
            "token", DateTimeOffset.Now.AddHours(12),
            new CurrentUserDto(userId, $"agent-{extension}", "Agent", UserRoles.Agent),
            Guid.NewGuid(),
            new AgentExtensionsDto("pbx.test", extension, "secret")));

    private static LogCallRequest Call(string sipCallId, string extension) => new(
        sipCallId, extension, Directions.In, CommunicationStatuses.Answered, "0599123456", null,
        DateTimeOffset.Now, DateTimeOffset.Now, DateTimeOffset.Now, null, "laptop");

    private static SaveClassificationByCallRequest Classify(string sipCallId, string extension) => new(
        sipCallId, extension,
        new SaveClassificationRequest(
            Guid.NewGuid(), null, null, null, false, null, 1, JsonDocument.Parse("{}")));

    private static PendingUpload Row(LogCallRequest call, Guid? userId) => new()
    {
        Kind = PendingUploadKinds.Call,
        Payload = JsonSerializer.Serialize(call),
        Reference = $"{call.SipCallId}|{call.Extension}",
        CreatedAt = DateTimeOffset.Now,
        UserId = userId,
    };

    /// <summary>A buffer file of the test's own.</summary>
    private sealed class Buffer(string path) : IDbContextFactory<AgentBufferDbContext>
    {
        // No pooling, so the folder can be deleted when the test ends.
        public string ConnectionString { get; } = $"Data Source={path};Pooling=False";

        public AgentBufferDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<AgentBufferDbContext>().UseSqlite(ConnectionString).Options);
    }

    /// <summary>A buffer that cannot be opened: a full disk, a locked file.</summary>
    private sealed class BrokenBuffer : IDbContextFactory<AgentBufferDbContext>
    {
        public AgentBufferDbContext CreateDbContext() => throw new IOException("The disk is full");
    }

    /// <summary>
    /// The server's side of the queue: which calls and classifications it
    /// took, and the refusals the test asks for.
    /// </summary>
    private sealed class FakeServer
    {
        private readonly Dictionary<string, int> _attempts = [];

        public List<string> Logged { get; } = [];

        public List<string> Classified { get; } = [];

        /// <summary>Calls the server already has, as if reported earlier.</summary>
        public HashSet<string> Known { get; } = [];

        public Dictionary<string, HttpStatusCode> FailCall { get; } = [];

        public HashSet<string> NotYours { get; } = [];

        public bool Down { get; set; }

        public int Attempts(string sipCallId) => _attempts.GetValueOrDefault(sipCallId);

        public ApiClient Client(AgentSession session) => new StubServer(Answer).Client(session);

        private HttpResponseMessage Answer(HttpRequestMessage request)
        {
            if (Down)
            {
                throw new HttpRequestException("No route to host");
            }

            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

            if (path == "/api/communications/calls")
            {
                var call = JsonSerializer.Deserialize<LogCallRequest>(body, JsonSerializerOptions.Web)!;
                _attempts[call.SipCallId] = Attempts(call.SipCallId) + 1;

                if (NotYours.Contains(call.SipCallId))
                {
                    return StubServer.Problem(HttpStatusCode.Conflict, "extension_not_yours");
                }

                if (FailCall.TryGetValue(call.SipCallId, out var status))
                {
                    // A 400 or a 500 with no code, as model validation and an
                    // unhandled exception send them.
                    return StubServer.Status(status);
                }

                Logged.Add(call.SipCallId);
                Known.Add(call.SipCallId);
                return StubServer.Json(HttpStatusCode.OK, new { });
            }

            if (path == "/api/classifications/by-call")
            {
                var classification = JsonSerializer.Deserialize<SaveClassificationByCallRequest>(
                    body, JsonSerializerOptions.Web)!;

                if (!Known.Contains(classification.SipCallId))
                {
                    return StubServer.Problem(HttpStatusCode.NotFound, "call_not_found");
                }

                Classified.Add(classification.SipCallId);
                return StubServer.Json(HttpStatusCode.OK, new { });
            }

            return StubServer.Status(HttpStatusCode.InternalServerError);
        }
    }
}
