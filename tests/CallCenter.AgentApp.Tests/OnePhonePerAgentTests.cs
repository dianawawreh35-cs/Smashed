using System.IO;
using System.Net;
using System.Net.Http;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Sip;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.AgentLogs;
using CallCenter.Shared.Contracts.Auth;
using FluentAssertions;
using SIPSorcery.SIP;
using Xunit;

namespace CallCenter.AgentApp.Tests;

/// <summary>
/// The guards added after the evening of 27 Sep, when calls went to a second
/// copy of the app signed in as the same agent (N-05): what can be tested
/// without a PBX, a sound card or a window.
/// </summary>
public class OnePhonePerAgentTests
{
    // Guard 1: one copy per Windows sign-in.

    private static string NewName() => "CallCenterTest." + Guid.NewGuid().ToString("N");

    [Fact]
    public void The_first_copy_claims_it_and_a_second_does_not()
    {
        var name = NewName();

        using var first = SingleInstance.Claim(name);
        var second = SingleInstance.Claim(name);

        first.Should().NotBeNull();
        second.Should().BeNull("a second copy starts no phone");
    }

    [Fact]
    public void Once_the_first_copy_has_gone_the_next_start_is_the_first_again()
    {
        // What a crash does too: Windows closes the handles of a process that
        // ends, however it ends, and the name goes with the last one.
        var name = NewName();

        SingleInstance.Claim(name)!.Dispose();

        using var next = SingleInstance.Claim(name);
        next.Should().NotBeNull();
    }

    [Fact]
    public async Task A_second_start_asks_the_first_copy_to_come_forward()
    {
        var name = NewName();
        using var first = SingleInstance.Claim(name)!;

        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.OnShowRequested(() => asked.TrySetResult());

        SingleInstance.Claim(name).Should().BeNull();

        await asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // Guard 3's other half: the laptop id tells laptops apart.

    [Fact]
    public void The_laptop_id_is_the_machine_name_and_a_tag_from_the_install_id()
    {
        var id = Guid.Parse("7f3a2c91-0000-4000-8000-000000000000");

        LaptopInfo.Compose("DESKTOP-RMSFSIV", id).Should().Be("DESKTOP-RMSFSIV-7F3A2C");
    }

    [Fact]
    public void Three_laptops_with_one_Windows_name_get_three_laptop_ids()
    {
        var ids = Enumerable.Range(0, 3)
            .Select(_ => LaptopInfo.Compose("DESKTOP-RMSFSIV", Guid.NewGuid()))
            .ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => AgentLogNames.Laptop(id) == id,
            "the log folder on the server is named after it, and needs no cleaning (N-12)");
    }

    [Fact]
    public void The_install_id_is_made_once_and_kept()
    {
        var directory = Path.Combine(Path.GetTempPath(), "install-id-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, LaptopInfo.InstallIdFileName);

        try
        {
            var made = LaptopInfo.ReadOrCreateInstallId(path);

            LaptopInfo.ReadOrCreateInstallId(path).Should().Be(made, "an update or a restart is the same install");

            File.WriteAllText(path, "not a guid");
            LaptopInfo.ReadOrCreateInstallId(path).Should().NotBe(made).And.NotBe(Guid.Empty,
                "a damaged file is replaced, not trusted");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Guard 3, in the app: a 401 says why the session closed.

    [Fact]
    public async Task A_refused_request_passes_on_the_reason_the_server_gave()
    {
        var session = SignedIn();
        string? heard = "nothing";
        session.TokenRefused += (_, because) => heard = because;

        var server = new StubServer(_ =>
        {
            var refused = StubServer.Status(HttpStatusCode.Unauthorized);
            refused.Headers.Add(LogoutReasons.SessionClosedHeader, LogoutReasons.SignedInElsewhere);
            return refused;
        });

        await server.Client(session).GetCurrentUserAsync();

        heard.Should().Be(LogoutReasons.SignedInElsewhere);
    }

    [Fact]
    public async Task A_refusal_that_says_nothing_passes_on_no_reason()
    {
        var session = SignedIn();
        string? heard = "nothing";
        session.TokenRefused += (_, because) => heard = because;

        await new StubServer(_ => StubServer.Status(HttpStatusCode.Unauthorized))
            .Client(session).GetCurrentUserAsync();

        heard.Should().BeNull("a changed password, say, is an ordinary sign-out, with an un-REGISTER");
    }

    private static AgentSession SignedIn()
    {
        var session = new AgentSession();
        session.SignIn(new LoginResponse(
            "token", DateTimeOffset.UtcNow.AddHours(8),
            new CurrentUserDto(Guid.NewGuid(), "sara", "Sara", UserRoles.Agent), Guid.NewGuid(), null));
        return session;
    }

    // Guard 2: the PBX forgets every old address at sign-in.

    [Fact]
    public void Forgetting_every_address_is_a_REGISTER_with_Contact_star_and_Expires_0()
    {
        var request = RegisterRequests.RemoveAll("2008", "192.168.0.27");
        var text = request.ToString();

        request.Method.Should().Be(SIPMethodsEnum.REGISTER);
        request.URI.ToString().Should().Be("sip:192.168.0.27");
        request.Header.To.ToURI.User.Should().Be("2008");
        request.Header.From.FromURI.User.Should().Be("2008");
        request.Header.Expires.Should().Be(0);

        ContactLines(text).Should().Equal("Contact: *");
        text.Should().Contain("Expires: 0");
    }

    [Fact]
    public void The_answer_to_the_PBX_s_challenge_is_a_new_transaction_still_forgetting_everything()
    {
        var request = RegisterRequests.RemoveAll("2008", "192.168.0.27");
        var challenge = SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.Unauthorised, null);
        challenge.Header.AuthenticationHeaders.Add(SIPAuthenticationHeader.ParseSIPAuthenticationHeader(
            SIPAuthorisationHeadersEnum.WWWAuthenticate, "Digest realm=\"asterisk\", nonce=\"5f2a\", algorithm=MD5"));

        var answered = RegisterRequests.Authenticated(request, challenge, "2008", "secret");
        var text = answered.ToString();

        answered.Header.CSeq.Should().Be(request.Header.CSeq + 1);
        answered.Header.CallId.Should().Be(request.Header.CallId);
        answered.Header.Vias.TopViaHeader.Branch.Should().NotBe(request.Header.Vias.TopViaHeader.Branch);
        answered.Header.Expires.Should().Be(0);
        text.Should().Contain("Authorization: Digest");
        ContactLines(text).Should().Equal("Contact: *");
    }

    [Fact]
    public void The_un_REGISTER_names_the_one_address_this_app_registered()
    {
        var contact = new SIPContactHeader(null, SIPURI.ParseSIPURIRelaxed("2008@192.168.0.40:51000"));

        var request = RegisterRequests.Remove("2008", "192.168.0.27", contact);

        RegisterRequests.IsRemoveAll(request).Should().BeFalse();
        request.Header.Contact.Should().ContainSingle()
            .Which.ContactURI.ToString().Should().Be("sip:2008@192.168.0.40:51000");
        request.Header.Expires.Should().Be(0);
    }

    private static List<string> ContactLines(string message) =>
        message.Split("\r\n")
            .Where(line => line.StartsWith("Contact", StringComparison.OrdinalIgnoreCase)
                           || line.StartsWith("m:", StringComparison.Ordinal))
            .ToList();

    // Guard 4: only this agent's calls.

    [Theory]
    [InlineData("2010", "2010", "2010", true)]
    [InlineData("2010", "2008", "2008", false)]
    [InlineData("2010", null, "2008", false)]
    [InlineData("2010", "2008", null, false)]
    [InlineData("2010", null, "2010", true)]
    [InlineData("2010", "2010", null, true)]
    [InlineData("2010", null, null, true)]
    [InlineData("2010", "", " ", true)]
    [InlineData("2010", "2010", "2008", true)]
    [InlineData("2010", "2008", "2010", true)]
    [InlineData(null, "2008", "2008", true)]
    public void A_call_is_refused_only_when_every_user_it_names_is_another_extension(
        string? extension, string? requestUriUser, string? toUser, bool isFor)
    {
        // Lenient on purpose: the Request-URI has not been seen on a real
        // INVITE yet, and a wrong guess must not silence the phone.
        CallAddressee.IsFor(extension, requestUriUser, toUser).Should().Be(isFor);
    }

    [Fact]
    public void The_extension_logged_is_the_Request_URI_s_and_else_the_To_header_s()
    {
        CallAddressee.Named("2008", "2009").Should().Be("2008");
        CallAddressee.Named(null, "2009").Should().Be("2009");
    }
}
