using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using CallCenter.AgentApp.Services;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.AgentLogs;
using CallCenter.Shared.Contracts.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CallCenter.AgentApp.Tests;

/// <summary>
/// The laptop's log reaches the server (N-12): whole lines, each once, and
/// whatever waited while the server was down.
/// </summary>
public sealed class AgentLogShipperTests : IDisposable
{
    private const string File = "agent-20260927.log";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "agent-log-ship-" + Guid.NewGuid().ToString("N"));

    private readonly FakeServer _server = new();

    private readonly AgentSession _session = new();

    public AgentLogShipperTests() => Directory.CreateDirectory(_directory);

    private AgentLogShipper Shipper() =>
        new(new Clients(_server), _session, NullLogger<AgentLogShipper>.Instance, _directory);

    private void SignIn() => _session.SignIn(new LoginResponse(
        "token", DateTimeOffset.UtcNow.AddHours(8),
        new CurrentUserDto(Guid.NewGuid(), "sara", "Sara", UserRoles.Agent), null, null));

    private void Write(string text, string file = File) =>
        System.IO.File.AppendAllText(Path.Combine(_directory, file), text);

    [Fact]
    public async Task Sends_whole_lines_and_then_the_rest()
    {
        SignIn();
        var shipper = Shipper();
        Write("one\ntwo\nthr");

        await shipper.ShipAsync();
        _server.Text(shipper.Laptop, File).Should().Be("one\ntwo\n", "the last line is still being written");

        Write("ee\n");
        await shipper.ShipAsync();
        _server.Text(shipper.Laptop, File).Should().Be("one\ntwo\nthree\n");
    }

    [Fact]
    public async Task Sends_nothing_while_nobody_is_signed_in_and_everything_after()
    {
        var shipper = Shipper();
        Write("before sign-in\n");

        await shipper.ShipAsync();
        _server.Requests.Should().Be(0);

        SignIn();
        await shipper.ShipAsync();
        _server.Text(shipper.Laptop, File).Should().Be("before sign-in\n");
    }

    [Fact]
    public async Task What_waited_while_the_server_was_down_goes_once_it_is_back()
    {
        SignIn();
        var shipper = Shipper();
        Write("one\n");
        await shipper.ShipAsync();

        _server.Down = true;
        Write("two\n");
        await shipper.ShipAsync();
        Write("three\n");
        await shipper.ShipAsync();

        _server.Down = false;
        await shipper.ShipAsync();
        _server.Text(shipper.Laptop, File).Should().Be("one\ntwo\nthree\n");
    }

    [Fact]
    public async Task A_piece_whose_answer_was_lost_is_not_written_twice()
    {
        SignIn();
        var shipper = Shipper();
        Write("one\n");

        _server.LoseNextAnswer = true;
        await shipper.ShipAsync();
        _server.Text(shipper.Laptop, File).Should().Be("one\n", "the server wrote it, the laptop never heard");

        Write("two\n");
        await shipper.ShipAsync();
        _server.Text(shipper.Laptop, File).Should().Be("one\ntwo\n");
    }

    [Fact]
    public async Task Picks_up_where_the_server_copy_ends_after_a_restart()
    {
        SignIn();
        Write("one\n");
        await Shipper().ShipAsync();

        Write("two\n");
        var restarted = Shipper();
        await restarted.ShipAsync();

        _server.Text(restarted.Laptop, File).Should().Be("one\ntwo\n");
    }

    [Fact]
    public async Task Every_day_on_the_laptop_is_sent_oldest_first()
    {
        SignIn();
        var shipper = Shipper();
        Write("yesterday\n", "agent-20260926.log");
        Write("today\n");
        Write("not a log\n", "notes.txt");

        await shipper.ShipAsync();

        _server.Text(shipper.Laptop, "agent-20260926.log").Should().Be("yesterday\n");
        _server.Text(shipper.Laptop, File).Should().Be("today\n");
        _server.Files.Should().HaveCount(2);
        _server.Order.Should().Equal("agent-20260926.log", File);
    }

    [Fact]
    public async Task A_refused_sign_in_is_passed_on_with_the_reason_the_server_gave()
    {
        // N-05: this sender talks to the server every 30 seconds, so it is how
        // a copy whose agent signed in on another laptop finds out soonest.
        SignIn();
        string? heard = null;
        _session.TokenRefused += (_, because) => heard = because;
        Write("one\n");

        _server.SessionClosed = LogoutReasons.SignedInElsewhere;
        await Shipper().ShipAsync();

        heard.Should().Be(LogoutReasons.SignedInElsewhere);
    }

    [Fact]
    public void A_piece_is_cut_at_its_last_line_break_and_a_giant_line_goes_whole()
    {
        var path = Path.Combine(_directory, File);
        System.IO.File.WriteAllBytes(path, Encoding.UTF8.GetBytes(new string('x', AgentLogNames.MaxChunkBytes + 10)));

        AgentLogShipper.ReadPiece(path, 0).Should().HaveCount(AgentLogNames.MaxChunkBytes);
        AgentLogShipper.ReadPiece(path, AgentLogNames.MaxChunkBytes).Should().BeNull("ten bytes and no line break yet");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class Clients(FakeServer server) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(server, disposeHandler: false) { BaseAddress = new Uri("http://server/") };
    }

    /// <summary>The server's side of the exchange, as <c>AgentLogStore</c> answers it.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly Dictionary<string, List<byte>> _files = new();

        public bool Down { get; set; }

        public bool LoseNextAnswer { get; set; }

        /// <summary>When set, every request is refused 401 with this as the session's closing reason.</summary>
        public string? SessionClosed { get; set; }

        public int Requests { get; private set; }

        public List<string> Order { get; } = [];

        public IReadOnlyCollection<string> Files => _files.Keys;

        public string Text(string laptop, string file) =>
            _files.TryGetValue($"{laptop}/{file}", out var bytes) ? Encoding.UTF8.GetString(bytes.ToArray()) : "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;

            if (Down)
            {
                throw new HttpRequestException("No route to host");
            }

            request.Headers.Authorization?.Parameter.Should().Be("token");

            if (SessionClosed is { } reason)
            {
                var refused = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                refused.Headers.Add(LogoutReasons.SessionClosedHeader, reason);
                return refused;
            }
            var parts = request.RequestUri!.AbsolutePath.Trim('/').Split('/').Skip(2).ToArray();

            if (request.Method == HttpMethod.Get)
            {
                var lengths = _files
                    .Where(f => f.Key.StartsWith(parts[0] + "/", StringComparison.Ordinal))
                    .ToDictionary(f => f.Key[(parts[0].Length + 1)..], f => (long)f.Value.Count);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(lengths) };
            }

            var key = $"{parts[0]}/{parts[1]}";
            var offset = long.Parse(request.RequestUri.Query.Split('=')[1]);
            var body = await request.Content!.ReadAsByteArrayAsync(ct);

            if (!_files.TryGetValue(key, out var file))
            {
                _files[key] = file = [];
            }

            if (file.Count != offset)
            {
                return new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = JsonContent.Create(new AgentLogLengthDto(file.Count)),
                };
            }

            file.AddRange(body);
            Order.Add(parts[1]);

            if (LoseNextAnswer)
            {
                LoseNextAnswer = false;
                throw new HttpRequestException("The connection was reset");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new AgentLogLengthDto(file.Count)),
            };
        }
    }
}
