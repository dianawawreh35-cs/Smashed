using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using CallCenter.Shared.Phone;
using CallCenter.Shared.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The supervisor's search across every call and one call opened (S-02, S-03),
/// against a real database.
/// </summary>
/// <remarks>
/// Each test builds its own agent with three calls, and every search it makes
/// is narrowed to that agent. The rest of the table, which is whatever earlier
/// runs left, can never leak into an answer.
/// </remarks>
[Collection(ApiCollection.Name)]
public class CallSearchTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task An_agent_is_refused_the_search_and_the_details()
    {
        // A-52: agents never see each other's calls. Their own are in their
        // own log; this door is the supervisor's.
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());

        (await agent.GetAsync("/api/communications/search")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.GetAsync($"/api/communications/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [DatabaseFact]
    public async Task An_agent_s_calls_come_back_newest_first_with_who_what_and_how_much()
    {
        var s = await ScenarioAsync();

        var page = await s.SearchAsync("");

        page.Total.Should().Be(3);
        page.Rows.Select(r => r.Id).Should().Equal(s.NoAnswer, s.Answered, s.Missed);

        var answered = page.Rows[1];
        answered.AgentDisplayName.Should().Be(s.AgentName);
        answered.ContactName.Should().Be("سارة الحلبي");
        answered.BranchName.Should().Be(s.BranchName);
        answered.TypeName.Should().Be(s.TypeName);
        answered.OrderValue.Should().Be(45.50m);
        answered.Notes.Should().Be("cold fries", "a classified call shows its classification's notes");
        answered.IsClassified.Should().BeTrue();
        answered.HasRecording.Should().BeTrue();

        page.Rows[0].Notes.Should().Be("try after 6", "an unclassified call shows its own note");
        page.Rows[2].RecordingExpired.Should().BeTrue();
        page.Rows[2].HasRecording.Should().BeFalse();
    }

    [DatabaseFact]
    public async Task Pages_split_the_matches_and_the_total_counts_all_of_them()
    {
        var s = await ScenarioAsync();

        var first = await s.SearchAsync("&pageSize=2&page=1");
        var second = await s.SearchAsync("&pageSize=2&page=2");

        first.Total.Should().Be(3);
        second.Total.Should().Be(3, "the total is every match, not the page");
        first.Rows.Should().HaveCount(2);
        second.Rows.Select(r => r.Id).Should().Equal(s.Missed);
    }

    [DatabaseTheory]
    [InlineData("national")]
    [InlineData("international")]
    [InlineData("dialled abroad")]
    [InlineData("partial")]
    public async Task A_number_finds_the_calls_from_it_in_whatever_form_it_is_typed(string form)
    {
        var s = await ScenarioAsync();
        var typed = form switch
        {
            "national" => s.CustomerMobile,                       // 0599…
            "international" => $"+970 {s.CustomerMobile[1..]}",    // as the PBX shows it
            "dialled abroad" => $"00970{s.CustomerMobile[1..]}",
            _ => s.CustomerMobile[3..8],                           // a piece from the middle
        };

        var page = await s.SearchAsync($"&q={Uri.EscapeDataString(typed)}");

        page.Rows.Select(r => r.Id).Should().BeEquivalentTo([s.Answered, s.Missed]);
    }

    [DatabaseFact]
    public async Task A_name_finds_the_contact_s_calls_however_its_Arabic_is_spelled()
    {
        var s = await ScenarioAsync();

        // Saved as "سارة", searched as "ساره": the normaliser folds the two (A-80).
        var page = await s.SearchAsync($"&q={Uri.EscapeDataString("ساره")}");

        page.Rows.Select(r => r.Id).Should().BeEquivalentTo([s.Answered, s.Missed]);
    }

    [DatabaseFact]
    public async Task Status_and_direction_narrow_the_list()
    {
        var s = await ScenarioAsync();

        (await s.SearchAsync($"&status={CommunicationStatuses.Missed}")).Rows.Select(r => r.Id).Should().Equal(s.Missed);
        (await s.SearchAsync($"&direction={Directions.Out}")).Rows.Select(r => r.Id).Should().Equal(s.NoAnswer);
    }

    [DatabaseFact]
    public async Task A_date_range_includes_its_start_and_excludes_its_end()
    {
        var s = await ScenarioAsync();

        // From three and a half hours ago up to (not including) the no-answer
        // call's start: only the answered call, which began three hours ago.
        var from = Uri.EscapeDataString(s.Now.AddHours(-3.5).ToString("o"));
        var to = Uri.EscapeDataString(s.Now.AddHours(-2).ToString("o"));

        (await s.SearchAsync($"&from={from}&to={to}")).Rows.Select(r => r.Id).Should().Equal(s.Answered);
    }

    [DatabaseFact]
    public async Task Type_branch_and_order_value_narrow_to_classified_calls()
    {
        var s = await ScenarioAsync();

        (await s.SearchAsync($"&typeId={s.TypeId}")).Rows.Select(r => r.Id).Should().Equal(s.Answered);
        (await s.SearchAsync($"&branchId={s.BranchId}")).Rows.Select(r => r.Id).Should().Equal(s.Answered);
        (await s.SearchAsync("&minOrder=40&maxOrder=50")).Rows.Select(r => r.Id).Should().Equal(s.Answered);
        (await s.SearchAsync("&minOrder=50")).Rows.Should().BeEmpty();
        (await s.SearchAsync("&classified=false")).Rows.Select(r => r.Id).Should().Equal(s.NoAnswer, s.Missed);
    }

    [DatabaseFact]
    public async Task Notes_text_searches_both_the_classification_and_the_call_s_own_note()
    {
        var s = await ScenarioAsync();

        (await s.SearchAsync("&notes=FRIES")).Rows.Select(r => r.Id).Should().Equal(s.Answered);
        (await s.SearchAsync("&notes=after%206")).Rows.Select(r => r.Id).Should().Equal(s.NoAnswer);
    }

    [DatabaseFact]
    public async Task Has_recording_finds_playable_audio_and_not_expired_audio()
    {
        var s = await ScenarioAsync();

        (await s.SearchAsync("&hasRecording=true")).Rows.Select(r => r.Id).Should().Equal(s.Answered);
        (await s.SearchAsync("&hasRecording=false")).Rows.Select(r => r.Id).Should().Equal(s.NoAnswer, s.Missed);
    }

    [DatabaseFact]
    public async Task A_call_opens_with_its_times_queue_extension_and_note()
    {
        var s = await ScenarioAsync();

        var details = await s.Supervisor.GetFromJsonAsync<CallDetailsDto>($"/api/communications/{s.NoAnswer}");

        details!.Summary.Id.Should().Be(s.NoAnswer);
        details.Summary.Direction.Should().Be(Directions.Out);
        details.CallNotes.Should().Be("try after 6");
        details.Extension.Should().Be("9100");
        details.QueueName.Should().Be("smashed-002");

        (await s.Supervisor.GetAsync($"/api/communications/{Guid.NewGuid()}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    // ---- the scenario ------------------------------------------------------

    private sealed record Scenario(
        HttpClient Supervisor,
        Guid AgentId,
        string AgentName,
        string CustomerMobile,
        Guid BranchId,
        string BranchName,
        Guid TypeId,
        string TypeName,
        Guid Answered,
        Guid NoAnswer,
        Guid Missed,
        DateTimeOffset Now)
    {
        public async Task<CallSearchPageDto> SearchAsync(string query)
        {
            var response = await Supervisor.GetAsync($"/api/communications/search?agentId={AgentId}{query}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<CallSearchPageDto>())!;
        }
    }

    /// <summary>
    /// One agent with three calls: an answered, classified, recorded call from a
    /// known customer; an outbound call nobody picked up, with a note; and a
    /// missed call from the same customer whose recording has expired.
    /// </summary>
    private async Task<Scenario> ScenarioAsync()
    {
        await data.EnsurePhoneChannelAsync();
        var agent = await data.CreateUserAsync();
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        var mobile = TestData.NewMobile();
        var contact = await data.CreateContactAsync("سارة الحلبي", mobile);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;

        var ids = await data.QueryAsync(async db =>
        {
            var channel = await db.Channels.Where(c => c.Name == ChannelNames.Phone).Select(c => c.Id).FirstAsync();

            // Names are unique in these tables, so each run brings its own.
            var branch = new Branch { Name = $"Test branch {suffix}" };
            var type = new ClassificationType { Name = $"TestType{suffix}", LabelAr = "طلب", LabelEn = "Order" };
            var form = new FormDefinition
            {
                Version = 100_000 + Random.Shared.Next(0, 1_000_000_000),
                Definition = JsonDocument.Parse("""{"fields":[]}"""),
            };
            db.AddRange(branch, type, form);
            await db.SaveChangesAsync();

            // Contact names are searched in their normalised form, as the
            // contacts service stores them.
            await db.Contacts.Where(c => c.Id == contact.Id).ExecuteUpdateAsync(x =>
                x.SetProperty(c => c.NameNormalised, NameNormalizer.Normalize("سارة الحلبي")));

            Communication Call(string direction, string status, DateTimeOffset started, string number) => new()
            {
                Kind = CommunicationKinds.Call,
                ChannelId = channel,
                Direction = direction,
                Status = status,
                AgentId = agent.Id,
                RemoteNumberRaw = number,
                RemoteNormalised = PhoneNormalizer.Normalize(number),
                StartedAt = started,
                Extension = "9100",
                QueueName = "smashed-002",
                SipCallId = TestData.NewSipCallId(),
                Source = CommunicationSources.AgentApp,
            };

            var answered = Call(Directions.In, CommunicationStatuses.Answered, now.AddHours(-3), mobile);
            answered.ContactId = contact.Id;
            answered.BranchId = branch.Id;
            answered.AnsweredAt = answered.StartedAt.AddSeconds(5);
            answered.EndedAt = answered.StartedAt.AddMinutes(2);
            answered.DurationSec = 115;

            var noAnswer = Call(Directions.Out, CommunicationStatuses.NoAnswer, now.AddHours(-2), TestData.NewMobile());
            noAnswer.Notes = "try after 6";

            var missed = Call(Directions.In, CommunicationStatuses.Missed, now.AddDays(-1), mobile);
            missed.ContactId = contact.Id;

            db.Communications.AddRange(answered, noAnswer, missed);
            await db.SaveChangesAsync();

            db.Classifications.Add(new Classification
            {
                CommunicationId = answered.Id,
                TypeId = type.Id,
                OrderValue = 45.50m,
                Notes = "cold fries",
                FormVersion = form.Version,
                CustomValues = JsonDocument.Parse("{}"),
                ClassifiedBy = agent.Id,
                ClassifiedAt = now,
            });

            db.Recordings.Add(new Recording { CommunicationId = answered.Id, Path = "test/answered.wav", UploadedAt = now });
            db.Recordings.Add(new Recording
            {
                CommunicationId = missed.Id, Path = "test/missed.wav", UploadedAt = now.AddDays(-1), DeletedAt = now,
            });
            await db.SaveChangesAsync();

            return (Answered: answered.Id, NoAnswer: noAnswer.Id, Missed: missed.Id,
                BranchId: branch.Id, BranchName: branch.Name, TypeId: type.Id, TypeName: type.Name);
        });

        return new Scenario(
            supervisor, agent.Id, agent.DisplayName, mobile,
            ids.BranchId, ids.BranchName, ids.TypeId, ids.TypeName,
            ids.Answered, ids.NoAnswer, ids.Missed, now);
    }
}
