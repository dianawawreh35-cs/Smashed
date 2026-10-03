using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Calls;
using CallCenter.Shared.Contracts.Breaks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CallCenter.AgentApp.Tests;

/// <summary>
/// The do-not-disturb switch reaches the server for the break monitor (A-18,
/// S-66): at sign-in, at every switch, again when the server was down, and not
/// again when the server cannot take it.
/// </summary>
public sealed class DoNotDisturbReporterTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"cc-dnd-{Guid.NewGuid():N}");
    private readonly List<SaveDoNotDisturbRequest> _sent = [];
    private readonly List<DoNotDisturbReporter> _reporters = [];

    private DateTimeOffset _now = new(2026, 10, 3, 18, 0, 0, TimeSpan.FromHours(3));

    /// <summary>What the stub server answers: 204, down, or anything else.</summary>
    private Func<HttpResponseMessage> _answer = () => StubServer.Status(HttpStatusCode.NoContent);

    public DoNotDisturbReporterTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        // Their retry timers stop with them.
        _reporters.ForEach(r => r.Dispose());

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Windows may still hold the file for a moment; it is in Temp.
        }
    }

    [Fact]
    public async Task Sign_in_sends_the_switch_as_it_is_and_each_switch_sends_the_new_state()
    {
        var (reporter, preferences) = Build();
        preferences.DoNotDisturb = true;

        reporter.Start();
        await reporter.SendAsync();

        _now = _now.AddMinutes(3);
        preferences.DoNotDisturb = false;
        await reporter.SendAsync();

        preferences.AutoAnswer = true;
        await reporter.SendAsync();

        _sent.Should().Equal(
            new SaveDoNotDisturbRequest(true, _now.AddMinutes(-3)),
            new SaveDoNotDisturbRequest(false, _now));
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_is_tried_again_with_the_same_time()
    {
        var (reporter, preferences) = Build();
        _answer = () => throw new HttpRequestException("down");

        reporter.Start();
        await reporter.SendAsync();
        _sent.Should().BeEmpty();

        _answer = () => StubServer.Status(HttpStatusCode.NoContent);
        var switchedAt = _now;
        _now = _now.AddMinutes(1);
        await reporter.SendAsync();

        _sent.Should().Equal(new SaveDoNotDisturbRequest(preferences.DoNotDisturb, switchedAt));
    }

    [Fact]
    public async Task A_server_too_old_to_know_it_is_not_asked_again()
    {
        var (reporter, _) = Build();
        _answer = () => StubServer.Status(HttpStatusCode.NotFound);

        reporter.Start();
        await reporter.SendAsync();

        _answer = () => StubServer.Status(HttpStatusCode.NoContent);
        await reporter.SendAsync();

        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task After_sign_out_a_switch_is_not_sent()
    {
        var (reporter, preferences) = Build();

        reporter.Start();
        await reporter.SendAsync();
        reporter.Stop();

        preferences.DoNotDisturb = true;
        await reporter.SendAsync();

        _sent.Should().ContainSingle().Which.On.Should().BeFalse();
    }

    private (DoNotDisturbReporter Reporter, PhonePreferences Preferences) Build()
    {
        var settings = new AgentSettingsStore(NullLogger<AgentSettingsStore>.Instance)
        {
            FilePath = Path.Combine(_folder, "settings.json"),
        };
        var preferences = new PhonePreferences(settings, NullLogger<PhonePreferences>.Instance);

        var server = new StubServer(request =>
        {
            var answer = _answer();

            if (answer.IsSuccessStatusCode)
            {
                _sent.Add(request.Content!.ReadFromJsonAsync<SaveDoNotDisturbRequest>().GetAwaiter().GetResult()!);
            }

            return answer;
        });

        var reporter = new DoNotDisturbReporter(server.Client(new AgentSession()), preferences,
            NullLogger<DoNotDisturbReporter>.Instance)
        {
            Now = () => _now,
        };

        _reporters.Add(reporter);
        return (reporter, preferences);
    }
}
