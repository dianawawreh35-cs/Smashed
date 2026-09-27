using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.AgentAppInstaller;
using CallCenter.Server.Features.Auth;
using CallCenter.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The Agent App installer served from the server (N-11, S-63): a supervisor
/// uploads it, and any signed-in account downloads it.
/// </summary>
/// <remarks>
/// Each test has a host of its own with an empty folder, so what one uploads
/// is never what another finds. None needs PostgreSQL.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class AgentAppInstallerTests : IDisposable
{
    /// <summary>The first bytes of any Windows program, and a little more.</summary>
    private static readonly byte[] SomeProgram = [(byte)'M', (byte)'Z', 0x90, 0x00, 1, 2, 3, 4];

    /// <summary>The first bytes of any zip, and a little more.</summary>
    private static readonly byte[] SomeZip = [(byte)'P', (byte)'K', 3, 4, 20, 0, 5, 6, 7];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "callcenter-tests", $"agent-app-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _host;

    public AgentAppInstallerTests(CallCenterApiFactory factory)
    {
        _host = factory.WithWebHostBuilder(b => b.UseSetting("AgentAppInstaller:Path", _folder));
    }

    public void Dispose()
    {
        _host.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("GET", "/api/agent-app")]
    [InlineData("GET", "/api/agent-app/installer")]
    [InlineData("PUT", "/api/agent-app/installer?version=0.4.1")]
    [InlineData("GET", "/api/agent-app/zip")]
    [InlineData("PUT", "/api/agent-app/zip?version=0.4.1")]
    public async Task Without_a_token_nothing_is_served(string method, string path)
    {
        var response = await _host.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_agent_cannot_upload()
    {
        var response = await Upload(ClientFor(UserRoles.Agent), SomeProgram, "0.4.1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Directory.Exists(_folder).Should().BeFalse("nothing was written");
    }

    [Fact]
    public async Task Before_the_first_upload_there_is_nothing_to_offer()
    {
        var agent = ClientFor(UserRoles.Agent);

        var current = await agent.GetAsync("/api/agent-app");
        var download = await agent.GetAsync("/api/agent-app/installer");

        current.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await current.Content.ReadAsStringAsync()).Should().Contain("no_installer");
        download.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task What_a_supervisor_uploads_an_agent_downloads()
    {
        var uploaded = await Upload(ClientFor(UserRoles.Supervisor), SomeProgram, "0.4.1");
        uploaded.StatusCode.Should().Be(HttpStatusCode.OK);

        var agent = ClientFor(UserRoles.Agent);

        var current = await agent.GetFromJsonAsync<AgentAppInstallerDto>("/api/agent-app");
        current!.Version.Should().Be("0.4.1");
        current.FileName.Should().Be("SmashedAgentApp-Setup-0.4.1.exe");
        current.SizeBytes.Should().Be(SomeProgram.Length);
        current.UploadedBy.Should().Be("supervisor");

        var download = await agent.GetAsync("/api/agent-app/installer");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(SomeProgram);
        download.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("SmashedAgentApp-Setup-0.4.1.exe");
        download.Headers.CacheControl!.NoStore.Should().BeTrue("the same address serves the next version");
    }

    [Fact]
    public async Task A_new_upload_replaces_the_last()
    {
        var supervisor = ClientFor(UserRoles.Supervisor);
        byte[] newer = [(byte)'M', (byte)'Z', 9, 9];

        await Upload(supervisor, SomeProgram, "0.4.1");
        await Upload(supervisor, newer, "0.4.2");

        var current = await supervisor.GetFromJsonAsync<AgentAppInstallerDto>("/api/agent-app");
        current!.Version.Should().Be("0.4.2");
        (await supervisor.GetByteArrayAsync("/api/agent-app/installer")).Should().Equal(newer);
        Directory.GetFiles(_folder).Select(Path.GetFileName)
            .Should().BeEquivalentTo(["SmashedAgentApp-Setup.exe", "current.json"], "one installer, and no upload left half-written");
    }

    [Fact]
    public async Task A_file_that_is_not_a_program_is_refused_and_the_last_one_kept()
    {
        var supervisor = ClientFor(UserRoles.Supervisor);
        await Upload(supervisor, SomeProgram, "0.4.1");

        // The zip, picked by mistake: it starts "PK".
        var response = await Upload(supervisor, [(byte)'P', (byte)'K', 3, 4], "0.4.2");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("not_a_program");

        var current = await supervisor.GetFromJsonAsync<AgentAppInstallerDto>("/api/agent-app");
        current!.Version.Should().Be("0.4.1");
        Directory.GetFiles(_folder).Should().HaveCount(2, "the refused upload's temporary file is gone");
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("0.4")]
    [InlineData("../../etc")]
    [InlineData("0.4.1-test")]
    public async Task A_version_that_is_not_one_is_refused(string version)
    {
        var response = await Upload(ClientFor(UserRoles.Supervisor), SomeProgram, version);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("bad_version");
    }

    [Fact]
    public async Task The_zip_goes_beside_the_installer_and_an_agent_downloads_it()
    {
        var supervisor = ClientFor(UserRoles.Supervisor);
        await Upload(supervisor, SomeProgram, "0.4.1");

        var uploaded = await Upload(supervisor, SomeZip, "0.4.1", "zip");
        uploaded.StatusCode.Should().Be(HttpStatusCode.OK);

        var agent = ClientFor(UserRoles.Agent);
        var current = await agent.GetFromJsonAsync<AgentAppInstallerDto>("/api/agent-app");
        current!.ZipFileName.Should().Be("SmashedAgentApp-0.4.1.zip");
        current.ZipSizeBytes.Should().Be(SomeZip.Length);
        current.FileName.Should().Be("SmashedAgentApp-Setup-0.4.1.exe", "the installer is still offered");

        var download = await agent.GetAsync("/api/agent-app/zip");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(SomeZip);
        download.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("SmashedAgentApp-0.4.1.zip");
    }

    [Fact]
    public async Task Without_a_zip_there_is_none_to_download()
    {
        await Upload(ClientFor(UserRoles.Supervisor), SomeProgram, "0.4.1");

        var agent = ClientFor(UserRoles.Agent);
        var current = await agent.GetFromJsonAsync<AgentAppInstallerDto>("/api/agent-app");
        var download = await agent.GetAsync("/api/agent-app/zip");

        current!.ZipFileName.Should().BeNull();
        download.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await download.Content.ReadAsStringAsync()).Should().Contain("no_zip");
    }

    [Fact]
    public async Task A_zip_needs_an_installer_of_its_own_version()
    {
        var supervisor = ClientFor(UserRoles.Supervisor);

        var first = await Upload(supervisor, SomeZip, "0.4.1", "zip");
        first.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await first.Content.ReadAsStringAsync()).Should().Contain("no_installer");

        await Upload(supervisor, SomeProgram, "0.4.1");
        var older = await Upload(supervisor, SomeZip, "0.4.0", "zip");

        older.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await older.Content.ReadAsStringAsync()).Should().Contain("version_mismatch");
    }

    [Fact]
    public async Task A_new_installer_takes_the_old_zip_away()
    {
        // Otherwise the fallback beside 0.4.2 would be 0.4.1.
        var supervisor = ClientFor(UserRoles.Supervisor);
        await Upload(supervisor, SomeProgram, "0.4.1");
        await Upload(supervisor, SomeZip, "0.4.1", "zip");

        await Upload(supervisor, SomeProgram, "0.4.2");

        var current = await supervisor.GetFromJsonAsync<AgentAppInstallerDto>("/api/agent-app");
        current!.Version.Should().Be("0.4.2");
        current.ZipFileName.Should().BeNull();
        (await supervisor.GetAsync("/api/agent-app/zip")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_installer_is_refused_as_the_zip_and_an_agent_cannot_upload_one()
    {
        var supervisor = ClientFor(UserRoles.Supervisor);
        await Upload(supervisor, SomeProgram, "0.4.1");

        var wrongFile = await Upload(supervisor, SomeProgram, "0.4.1", "zip");
        var agent = await Upload(ClientFor(UserRoles.Agent), SomeZip, "0.4.1", "zip");

        wrongFile.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrongFile.Content.ReadAsStringAsync()).Should().Contain("not_a_zip");
        agent.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Directory.GetFiles(_folder).Should().HaveCount(2, "no zip, and no temporary file left behind");
    }

    [Theory]
    [InlineData("0.4.1", true)]
    [InlineData("0.3.2.1", true)]
    [InlineData("10.20.300", true)]
    [InlineData("0.4", false)]
    [InlineData("0.4.1.2.3", false)]
    [InlineData(" 0.4.1", false)]
    public void Version_numbers(string version, bool valid) =>
        AgentAppInstallerStore.IsVersion(version).Should().Be(valid);

    private static Task<HttpResponseMessage> Upload(
        HttpClient client, byte[] bytes, string version, string what = "installer")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return client.PutAsync($"/api/agent-app/{what}?version={Uri.EscapeDataString(version)}", content);
    }

    private HttpClient ClientFor(string role)
    {
        var tokens = new TokenService(
            Options.Create(new JwtOptions { SigningKey = CallCenterApiFactory.SigningKey }),
            TimeProvider.System);

        var (token, _) = tokens.Issue(
            new User
            {
                Id = Guid.NewGuid(),
                Login = role.ToLowerInvariant(),
                DisplayName = role,
                Role = role,
                PasswordHash = "unused",
            },
            sessionId: null);

        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
