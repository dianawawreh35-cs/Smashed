using System.IO;
using System.Net;
using System.Net.Http;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.ViewModels;
using CallCenter.Shared.Contracts.AgentApp;
using FluentAssertions;
using Xunit;

namespace CallCenter.AgentApp.Tests;

/// <summary>
/// The update bar (A-82) offers only a version that is really newer, and runs
/// only an installer that arrived whole.
/// </summary>
public sealed class AppUpdateTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "callcenter-tests", "update-" + Guid.NewGuid().ToString("N"));

    public AppUpdateTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("0.5.4", "0.5.3", true)]
    [InlineData("0.10.0", "0.9.9", true)]
    [InlineData("1.0.0", "0.5.3", true)]
    [InlineData("0.5.3", "0.5.3", false)]
    [InlineData("0.5.2", "0.5.3", false)]
    [InlineData("not a version", "0.5.3", false)]
    [InlineData("0.5.4", "0.0.0-dev", false)]
    public void Only_a_newer_version_is_offered(string offered, string own, bool expected) =>
        AppUpdateViewModel.IsNewer(offered, own).Should().Be(expected);

    [Fact]
    public async Task The_server_says_which_version_is_current()
    {
        var server = new StubServer(_ => StubServer.Json(HttpStatusCode.OK, new AgentAppInstallerDto(
            "0.5.4", "SmashedAgentApp-Setup-0.5.4.exe", 1234, DateTimeOffset.UtcNow, "dia20")));

        var result = await server.Client(new AgentSession()).GetAgentAppInstallerAsync();

        result.IsOk.Should().BeTrue();
        result.Value!.Version.Should().Be("0.5.4");
        server.Requests.Should().Equal("GET /api/agent-app");
    }

    [Fact]
    public async Task An_installer_that_arrives_whole_is_kept()
    {
        var program = new byte[4096];
        Random.Shared.NextBytes(program);
        var server = new StubServer(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(program) });
        var path = Path.Combine(_folder, "setup.exe");

        var result = await server.Client(new AgentSession()).DownloadAgentAppInstallerAsync(path, program.Length);

        result.IsOk.Should().BeTrue();
        File.ReadAllBytes(path).Should().Equal(program);
        Directory.GetFiles(_folder).Should().ContainSingle("the .part file is moved, not copied");
    }

    [Fact]
    public async Task An_installer_of_the_wrong_size_is_never_left_to_run()
    {
        // A supervisor uploading a newer version mid-download: the file is not
        // the one described.
        var server = new StubServer(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[100]) });
        var path = Path.Combine(_folder, "setup.exe");

        var result = await server.Client(new AgentSession()).DownloadAgentAppInstallerAsync(path, 4096);

        result.IsOk.Should().BeFalse();
        result.ErrorCode.Should().Be("installer_incomplete");
        Directory.GetFiles(_folder).Should().BeEmpty();
    }

    [Fact]
    public async Task No_installer_on_the_server_downloads_nothing()
    {
        var server = new StubServer(_ => StubServer.Problem(HttpStatusCode.NotFound, "no_installer"));
        var path = Path.Combine(_folder, "setup.exe");

        var result = await server.Client(new AgentSession()).DownloadAgentAppInstallerAsync(path, 4096);

        result.IsOk.Should().BeFalse();
        result.ErrorCode.Should().Be("no_installer");
        Directory.GetFiles(_folder).Should().BeEmpty();
    }

    [Fact]
    public void A_copy_outside_the_install_folder_is_not_the_installed_one() =>
        // The tests run from bin\, as a developer's build does.
        AppUpdateViewModel.IsInstalledCopy.Should().BeFalse();
}
