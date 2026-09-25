using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Classifications;
using CallCenter.Shared.Contracts.Communications;
using CallCenter.Shared.Contracts.Contacts;
using CallCenter.Shared.Phone;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Messages — the conversations that did not arrive by phone (A-70 to A-73) —
/// against a real database.
/// </summary>
/// <remarks>
/// A message is a <c>communications</c> row with three values never written
/// before this (kind App, direction None, status Logged), so the check
/// constraints are the likeliest thing to reject it, and every test here goes
/// through the database. Each scenario builds its own agent, channel and
/// customer, and narrows every search and report to that agent, so nothing
/// earlier runs left can leak in.
/// </remarks>
[Collection(ApiCollection.Name)]
public class ApplicationsTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    // ---- recording (A-70) --------------------------------------------------

    [DatabaseFact]
    public async Task A_message_is_a_communications_row_with_kind_App_direction_None_and_status_Logged()
    {
        var s = await ScenarioAsync();

        var response = await s.Agent.PostAsJsonAsync("/api/communications/applications", new RecordApplicationRequest(
            s.WhatsApp, s.CustomerMobile, null, null,
            new SaveClassificationRequest(s.OrderType, s.BranchId, 42.5m, "two burgers", FollowUp: false)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var saved = (await response.Content.ReadFromJsonAsync<CommunicationDto>())!;

        saved.Kind.Should().Be(CommunicationKinds.App);
        saved.Direction.Should().Be(Directions.None);
        saved.Status.Should().Be(CommunicationStatuses.Logged);
        saved.ChannelName.Should().Be(s.WhatsAppName);
        saved.ContactId.Should().Be(s.ContactId, "the number is matched the way the pop-up matches a caller (A-13)");
        saved.IsClassified.Should().BeTrue();
        saved.CanBeClassified.Should().BeTrue();

        var row = await data.QueryAsync(db => db.Communications
            .Include(c => c.Classification)
            .FirstAsync(c => c.Id == saved.Id));

        row.Source.Should().Be(CommunicationSources.Manual);
        row.AgentId.Should().Be(s.AgentId);
        row.BranchId.Should().Be(s.BranchId, "the branch belongs to the communication, as for a call");
        row.RemoteNormalised.Should().Be(PhoneNormalizer.Normalize(s.CustomerMobile));
        row.Classification!.OrderValue.Should().Be(42.5m);
        row.Classification.Notes.Should().Be("two burgers");
    }

    [DatabaseFact]
    public async Task A_refused_classification_leaves_no_message_behind()
    {
        var s = await ScenarioAsync();
        var before = await s.CountMessagesAsync();

        // A type that does not exist: the classification is refused, and the
        // message must not be half-recorded without it.
        var response = await s.Agent.PostAsJsonAsync("/api/communications/applications", new RecordApplicationRequest(
            s.WhatsApp, s.CustomerMobile, null, null,
            new SaveClassificationRequest(Guid.NewGuid(), s.BranchId, null, null)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(response)).Should().Be("unknown_type");
        (await s.CountMessagesAsync()).Should().Be(before);
    }

    [DatabaseFact]
    public async Task A_message_is_never_recorded_without_its_type()
    {
        // Dia, 25 Sep: a message is typed by an agent who knows what it was,
        // so, unlike a call, it is never left unclassified. Refused, and
        // nothing is written.
        var s = await ScenarioAsync();
        var before = await s.CountMessagesAsync();

        var response = await s.Agent.PostAsJsonAsync("/api/communications/applications",
            new RecordApplicationRequest(s.WhatsApp, s.CustomerMobile, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(response)).Should().Be("classification_required");
        (await s.CountMessagesAsync()).Should().Be(before);
    }

    [DatabaseFact]
    public async Task The_Phone_channel_and_a_message_with_nobody_on_it_are_refused()
    {
        var s = await ScenarioAsync();

        var phone = await s.Agent.PostAsJsonAsync("/api/communications/applications",
            new RecordApplicationRequest(s.Phone, s.CustomerMobile, null, null, null));
        phone.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(phone)).Should().Be("phone_channel", "a phone conversation is a call");

        var nobody = await s.Agent.PostAsJsonAsync("/api/communications/applications",
            new RecordApplicationRequest(s.WhatsApp, null, null, null, null));
        nobody.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(nobody)).Should().Be("no_customer");
    }

    [DatabaseFact]
    public async Task A_chosen_contact_wins_and_lends_its_number_when_none_was_typed()
    {
        var s = await ScenarioAsync();

        // Some apps never show a number: the agent picks the contact instead.
        var response = await s.Agent.PostAsJsonAsync("/api/communications/applications",
            new RecordApplicationRequest(s.WhatsApp, null, s.ContactId, null, s.Classified));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var saved = (await response.Content.ReadFromJsonAsync<CommunicationDto>())!;

        saved.ContactId.Should().Be(s.ContactId);
        saved.RemoteNumberRaw.Should().Be(s.CustomerMobile);
        saved.IsClassified.Should().BeTrue();
    }

    [DatabaseFact]
    public async Task An_agent_may_set_the_time_back_within_today_but_not_forward_or_to_another_day()
    {
        var s = await ScenarioAsync();
        var now = DateTimeOffset.UtcNow;

        // Recorded at 11:20 about a message from 11:00: the reports keep the hour.
        var earlier = await s.Agent.PostAsJsonAsync("/api/communications/applications",
            new RecordApplicationRequest(s.WhatsApp, s.CustomerMobile, null, now.AddMinutes(-1), s.Classified));
        earlier.StatusCode.Should().Be(HttpStatusCode.OK, await earlier.Content.ReadAsStringAsync());

        var future = await s.Agent.PostAsJsonAsync("/api/communications/applications",
            new RecordApplicationRequest(s.WhatsApp, s.CustomerMobile, null, now.AddHours(2), s.Classified));
        future.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(future)).Should().Be("bad_time");

        var otherDay = await s.Agent.PostAsJsonAsync("/api/communications/applications",
            new RecordApplicationRequest(s.WhatsApp, s.CustomerMobile, null, now.AddDays(-2), s.Classified));
        otherDay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(otherDay)).Should().Be("bad_time");

        // Other days are the supervisor's.
        var supervisorBackfill = await s.Supervisor.PostAsJsonAsync("/api/communications/applications",
            new RecordApplicationRequest(s.WhatsApp, s.CustomerMobile, null, now.AddDays(-2), s.Classified));
        supervisorBackfill.StatusCode.Should().Be(HttpStatusCode.OK, await supervisorBackfill.Content.ReadAsStringAsync());
    }

    // ---- editing (A-71, A-42) ----------------------------------------------

    [DatabaseFact]
    public async Task An_agent_edits_today_s_message_and_not_yesterday_s_and_a_supervisor_edits_any()
    {
        var s = await ScenarioAsync();
        var today = await s.MessageAsync(s.WhatsApp, DateTimeOffset.UtcNow.AddMinutes(-10));
        var yesterday = await s.MessageAsync(s.WhatsApp, DateTimeOffset.UtcNow.AddDays(-1));
        var edit = new EditApplicationRequest(s.Facebook, s.CustomerMobile, null, null);

        // The rule under test is the default window (A-42). A development
        // database may have it widened to Always; put it back for this test
        // and restore it after.
        var window = await SetEditWindowAsync("SameDay");
        try
        {
            await AssertEditWindowAsync(s, today, yesterday, edit);
        }
        finally
        {
            await SetEditWindowAsync(window);
        }
    }

    private async Task AssertEditWindowAsync(Scenario s, Guid today, Guid yesterday, EditApplicationRequest edit)
    {
        var ok = await s.Agent.PutAsJsonAsync($"/api/communications/applications/{today}", edit);
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        (await ok.Content.ReadFromJsonAsync<CommunicationDto>())!.ChannelName.Should().Be(s.FacebookName);

        var closed = await s.Agent.PutAsJsonAsync($"/api/communications/applications/{yesterday}", edit);
        closed.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Code(closed)).Should().Be("edit_window_closed");

        var (other, _) = await data.SignInAsync(await data.CreateUserAsync());
        var notYours = await other.PutAsJsonAsync($"/api/communications/applications/{today}", edit);
        notYours.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Code(notYours)).Should().Be("not_your_call");

        var supervisor = await s.Supervisor.PutAsJsonAsync($"/api/communications/applications/{yesterday}", edit);
        supervisor.StatusCode.Should().Be(HttpStatusCode.OK, await supervisor.Content.ReadAsStringAsync());

        // A call is not a message, whoever asks.
        var call = await s.Supervisor.PutAsJsonAsync($"/api/communications/applications/{s.AnsweredCall}", edit);
        call.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(call)).Should().Be("not_an_application");
    }

    // ---- classifying a message (A-70, the not_answered fix) ---------------

    [DatabaseFact]
    public async Task A_message_is_classified_through_the_classification_endpoint_on_the_Applications_form()
    {
        var s = await ScenarioAsync();
        var message = await s.MessageAsync(s.WhatsApp, DateTimeOffset.UtcNow.AddMinutes(-5));

        // The third form, under direction None (Dia, 25 Sep).
        var form = await s.Agent.GetFromJsonAsync<ClassificationFormDto>("/api/classifications/form?direction=None");
        form!.Direction.Should().Be(Directions.None);
        form.Definition.RootElement.GetProperty("fields").GetArrayLength().Should().BeGreaterThan(0);

        // Logged, never Answered: the endpoint used to refuse this as not_answered.
        var response = await s.Agent.PutAsJsonAsync($"/api/classifications/{message}",
            new SaveClassificationRequest(s.ComplaintType, s.BranchId, null, "cold", FollowUp: true, FormVersion: form.Version));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var saved = (await response.Content.ReadFromJsonAsync<ClassificationDto>())!;
        saved.TypeId.Should().Be(s.ComplaintType);
        saved.FormVersion.Should().Be(form.Version);

        // The supervisor's third tab publishes to the same direction. Publishing
        // is real: it makes the new version current for every agent. So the
        // form that was current is put back afterwards, whatever happens, or a
        // test run would change the questions the restaurant's agents are asked
        // (it did, 25 Sep).
        int? publishedVersion = null;
        try
        {
            var published = await s.Supervisor.PutAsJsonAsync("/api/classifications/form",
                new PublishFormRequest(form.Definition, Directions.None));
            published.StatusCode.Should().Be(HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
            var dto = (await published.Content.ReadFromJsonAsync<ClassificationFormDto>())!;
            publishedVersion = dto.Version;
            dto.Direction.Should().Be(Directions.None);
        }
        finally
        {
            await RestoreCurrentFormAsync(Directions.None, form.Version, publishedVersion);
        }
    }

    [DatabaseFact]
    public async Task Each_form_offers_only_the_types_the_supervisor_chose_and_the_server_holds_to_it()
    {
        // S-40, Dia 25 Sep: which call types each form offers is chosen by hand.
        var s = await ScenarioAsync();
        var message = await s.MessageAsync(s.WhatsApp, DateTimeOffset.UtcNow.AddMinutes(-5));
        var current = (await s.Agent.GetFromJsonAsync<ClassificationFormDto>("/api/classifications/form?direction=None"))!;

        static JsonDocument Offering(params string[] names) => JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            fields = new object[]
            {
                new { key = "type", kind = "type", required = true, types = names },
                new { key = "notes", kind = "textarea" },
            },
        }));

        // A list that names no type, or a type that does not exist, is a form
        // nobody could save: refused, and the current form is untouched.
        foreach (var bad in new[] { Offering(), Offering("NoSuchType") })
        {
            var refused = await s.Supervisor.PutAsJsonAsync("/api/classifications/form", new PublishFormRequest(bad, Directions.None));
            refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Code(refused)).Should().Be("bad_form");
        }

        int? published = null;
        try
        {
            var response = await s.Supervisor.PutAsJsonAsync("/api/classifications/form",
                new PublishFormRequest(Offering("Complaint"), Directions.None));
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var form = (await response.Content.ReadFromJsonAsync<ClassificationFormDto>())!;
            published = form.Version;

            // The Applications form no longer offers Order: refused, for the
            // supervisor too, so no report counts an order the form does not ask.
            var order = await s.Supervisor.PutAsJsonAsync($"/api/classifications/{message}",
                new SaveClassificationRequest(s.OrderType, s.BranchId, 20m, null, FormVersion: form.Version));
            order.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Code(order)).Should().Be("type_not_offered");

            // Nor when the message is recorded with its classification.
            var recorded = await s.Agent.PostAsJsonAsync("/api/communications/applications", new RecordApplicationRequest(
                s.WhatsApp, s.CustomerMobile, null, null, new SaveClassificationRequest(s.OrderType, s.BranchId, 20m, null)));
            (await Code(recorded)).Should().Be("type_not_offered");

            var complaint = await s.Agent.PutAsJsonAsync($"/api/classifications/{message}",
                new SaveClassificationRequest(s.ComplaintType, s.BranchId, null, "cold", FormVersion: form.Version));
            complaint.StatusCode.Should().Be(HttpStatusCode.OK, await complaint.Content.ReadAsStringAsync());

            // Each form has its own list: an inbound call is still an order.
            var call = await s.Supervisor.PutAsJsonAsync($"/api/classifications/{s.AnsweredCall}",
                new SaveClassificationRequest(s.OrderType, s.BranchId, 30m, null));
            call.StatusCode.Should().Be(HttpStatusCode.OK, await call.Content.ReadAsStringAsync());
        }
        finally
        {
            await RestoreCurrentFormAsync(Directions.None, current.Version, published);
        }
    }

    /// <summary>
    /// Makes <paramref name="previous"/> the current form for its direction again
    /// and removes <paramref name="published"/>, which nothing was classified
    /// against. Cleared first: one current form per direction is a unique index.
    /// </summary>
    private Task RestoreCurrentFormAsync(string direction, int previous, int? published) => data.QueryAsync(async db =>
    {
        await db.FormDefinitions.Where(f => f.Direction == direction && f.IsCurrent)
            .ExecuteUpdateAsync(x => x.SetProperty(f => f.IsCurrent, false));
        await db.FormDefinitions.Where(f => f.Version == previous)
            .ExecuteUpdateAsync(x => x.SetProperty(f => f.IsCurrent, true));

        if (published is { } version && !await db.Classifications.AnyAsync(c => c.FormVersion == version))
        {
            await db.FormDefinitions.Where(f => f.Version == version).ExecuteDeleteAsync();
        }

        return 0;
    });

    // ---- the supervisor's search (S-02) -------------------------------------

    [DatabaseFact]
    public async Task The_supervisor_search_finds_messages_by_kind_and_channel_with_the_call_filters()
    {
        var s = await ScenarioAsync();
        var whatsApp = await s.MessageAsync(s.WhatsApp, DateTimeOffset.UtcNow.AddMinutes(-30), s.OrderType, 20m);
        var facebook = await s.MessageAsync(s.Facebook, DateTimeOffset.UtcNow.AddMinutes(-20), s.ComplaintType);

        var all = await s.SearchAsync("kind=App");
        all.Rows.Select(r => r.Id).Should().Equal(facebook, whatsApp);
        all.Rows.Should().OnlyContain(r => r.Kind == CommunicationKinds.App);
        all.Rows.First(r => r.Id == whatsApp).ChannelName.Should().Be(s.WhatsAppName);

        (await s.SearchAsync($"kind=App&channelId={s.Facebook}")).Rows.Select(r => r.Id).Should().Equal(facebook);
        (await s.SearchAsync($"kind=App&typeId={s.OrderType}")).Rows.Select(r => r.Id).Should().Equal(whatsApp);
        (await s.SearchAsync("kind=App&minOrder=10&maxOrder=25")).Rows.Select(r => r.Id).Should().Equal(whatsApp);
        (await s.SearchAsync($"kind=App&q={Uri.EscapeDataString(s.CustomerMobile)}")).Total.Should().Be(2);
        (await s.SearchAsync("kind=App&classified=false")).Rows.Should().BeEmpty();

        // Opened, as a call is.
        var details = await s.Supervisor.GetFromJsonAsync<CallDetailsDto>($"/api/communications/{whatsApp}");
        details!.Summary.Kind.Should().Be(CommunicationKinds.App);
        details.Summary.ChannelName.Should().Be(s.WhatsAppName);
    }

    // ---- calls-only screens stay calls only --------------------------------

    [DatabaseFact]
    public async Task Calls_only_screens_return_no_messages()
    {
        var s = await ScenarioAsync();
        var message = await s.MessageAsync(s.WhatsApp, DateTimeOffset.UtcNow.AddMinutes(-5), s.OrderType, 15m);

        // The Calls page: kind defaults to Call.
        var calls = await s.SearchAsync("");
        calls.Rows.Select(r => r.Id).Should().Contain(s.AnsweredCall).And.NotContain(message);
        calls.Rows.Should().OnlyContain(r => r.Kind == CommunicationKinds.Call);

        // The Agent App's call log.
        var log = await s.Agent.GetFromJsonAsync<List<CommunicationDto>>("/api/communications/mine");
        log!.Select(c => c.Id).Should().Contain(s.AnsweredCall).And.NotContain(message);

        // The caller card's recent calls. Its totals do count the WhatsApp
        // order: it is still one of this customer's orders.
        var card = await s.Agent.GetFromJsonAsync<CallerCardDto>($"/api/contacts/{s.ContactId}/card");
        card!.Recent.Should().OnlyContain(r => r.Status != CommunicationStatuses.Logged);
        card.Orders.Should().Be(2, "the phone order and the WhatsApp order");
    }

    [DatabaseFact]
    public async Task The_agent_s_own_list_has_their_messages_and_nothing_else()
    {
        var s = await ScenarioAsync();
        var mine = await s.MessageAsync(s.WhatsApp, DateTimeOffset.UtcNow.AddMinutes(-5));

        var other = await data.CreateUserAsync();
        var theirs = await s.MessageAsync(s.Facebook, DateTimeOffset.UtcNow.AddMinutes(-4), agentId: other.Id);

        var list = await s.Agent.GetFromJsonAsync<List<CommunicationDto>>("/api/communications/applications/mine");

        list!.Select(c => c.Id).Should().Contain(mine).And.NotContain(theirs).And.NotContain(s.AnsweredCall);
        list.Should().OnlyContain(c => c.Kind == CommunicationKinds.App);
        list.First(c => c.Id == mine).ChannelName.Should().Be(s.WhatsAppName);

        (await s.Agent.GetFromJsonAsync<List<CommunicationDto>>(
            $"/api/communications/applications/mine?q={Uri.EscapeDataString(s.CustomerMobile[3..8])}"))!
            .Select(c => c.Id).Should().Contain(mine);

        (await s.Supervisor.GetAsync("/api/communications/applications/mine")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "the list is an agent's own; the supervisor searches");
    }

    // ---- the contact's history (A-72) ---------------------------------------

    [DatabaseFact]
    public async Task A_message_appears_in_the_contact_s_history_beside_the_calls_with_its_channel()
    {
        var s = await ScenarioAsync();
        var message = await s.MessageAsync(s.WhatsApp, DateTimeOffset.UtcNow.AddMinutes(-5), s.OrderType, 15m);

        var history = await s.Supervisor.GetFromJsonAsync<List<CommunicationDto>>(
            $"/api/communications/by-contact/{s.ContactId}");

        history!.Select(c => c.Id).Should().Equal(message, s.AnsweredCall);

        var row = history[0];
        row.Kind.Should().Be(CommunicationKinds.App);
        row.ChannelName.Should().Be(s.WhatsAppName);
        row.IsClassified.Should().BeTrue();

        history[1].ChannelName.Should().Be(ChannelNames.Phone, "phone calls have Channel = Phone");
    }

    // ---- reports (A-72, R-12, R-13, S-07) -----------------------------------

    [DatabaseFact]
    public async Task The_reports_count_what_was_recorded_and_only_that()
    {
        var s = await ScenarioAsync();
        var now = DateTimeOffset.UtcNow;

        await s.MessageAsync(s.WhatsApp, now.AddMinutes(-50), s.OrderType, 20m);
        await s.MessageAsync(s.WhatsApp, now.AddMinutes(-40), s.OrderType, 30m);
        await s.MessageAsync(s.WhatsApp, now.AddMinutes(-30), s.ComplaintType);
        await s.MessageAsync(s.Facebook, now.AddMinutes(-20), s.CancellationType, 30m);
        await s.MessageAsync(s.Facebook, now.AddMinutes(-10));

        var byChannel = await s.ReportAsync<List<ChannelReportRowDto>>("by-channel");
        byChannel.Should().HaveCount(2);
        var whatsApp = byChannel.First(r => r.ChannelId == s.WhatsApp);
        whatsApp.Messages.Should().Be(3);
        whatsApp.ByType.Select(t => (t.TypeName, t.Count)).Should().Equal(("Order", 2), ("Complaint", 1));
        var facebook = byChannel.First(r => r.ChannelId == s.Facebook);
        facebook.Messages.Should().Be(2);

        // Orders: the cancellation's value is not revenue.
        var orders = await s.ReportAsync<List<OrdersReportRowDto>>("orders");
        orders.Should().ContainSingle();
        orders[0].Label.Should().Be(s.WhatsAppName);
        orders[0].Orders.Should().Be(2);
        orders[0].OrderValue.Should().Be(50m);
        orders[0].Average.Should().Be(25m);

        var byAgentOrders = await s.ReportAsync<List<OrdersReportRowDto>>("orders", "groupBy=agent");
        byAgentOrders.Should().ContainSingle().Which.Label.Should().Be(s.AgentName);

        var byDay = await s.ReportAsync<List<OrdersReportRowDto>>("orders", "groupBy=day");
        byDay.Sum(r => r.Orders).Should().Be(2);
        byDay.Should().OnlyContain(r => r.Key.Length == 10, "a day is yyyy-MM-dd");

        var trend = await s.ReportAsync<List<TrendPointDto>>("trend", "groupBy=day");
        trend.Sum(p => p.Messages).Should().Be(5);
        trend.Sum(p => p.Orders).Should().Be(2);
        trend.Sum(p => p.OrderValue).Should().Be(50m);

        var hours = await s.ReportAsync<List<TrendPointDto>>("trend", "groupBy=hour");
        hours.Sum(p => p.Messages).Should().Be(5);
        hours.Should().OnlyContain(p => p.Bucket.Length == 2);

        var byAgent = await s.ReportAsync<List<AgentReportRowDto>>("by-agent");
        var me = byAgent.Should().ContainSingle().Subject;
        me.AgentId.Should().Be(s.AgentId);
        me.Messages.Should().Be(5);
        me.Orders.Should().Be(2);
        me.OrderValue.Should().Be(50m);

        var issues = await s.ReportAsync<List<IssuesReportRowDto>>("issues");
        issues.First(r => r.ChannelId == s.WhatsApp).Complaints.Should().Be(1);
        issues.First(r => r.ChannelId == s.Facebook).Cancellations.Should().Be(1);

        // The common filters (S-07): channel, type, and a period that ends
        // before the last two messages.
        (await s.ReportAsync<List<ChannelReportRowDto>>("by-channel", $"channelId={s.Facebook}"))
            .Should().ContainSingle().Which.Channel.Should().Be(s.FacebookName);
        (await s.ReportAsync<List<ChannelReportRowDto>>("by-channel", $"typeId={s.OrderType}"))
            .Sum(r => r.Messages).Should().Be(2);
        var to = Uri.EscapeDataString(now.AddMinutes(-25).ToString("o"));
        var from = Uri.EscapeDataString(now.AddHours(-1).ToString("o"));
        (await s.ReportAsync<List<TrendPointDto>>("trend", $"from={from}&to={to}")).Sum(p => p.Messages).Should().Be(3);

        // Never calls: the answered phone order is not in any of these.
        (await s.ReportAsync<List<ChannelReportRowDto>>("by-channel")).Should().NotContain(r => r.Channel == ChannelNames.Phone);

        (await s.Agent.GetAsync($"/api/reports/applications/by-channel?agentId={s.AgentId}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "reports are the supervisor's");
    }

    // ---- channels (S-41) -----------------------------------------------------

    [DatabaseFact]
    public async Task Channels_are_added_renamed_reordered_and_hidden_and_Phone_is_neither_renamed_nor_hidden()
    {
        var s = await ScenarioAsync();
        var message = await s.MessageAsync(s.WhatsApp, DateTimeOffset.UtcNow.AddMinutes(-5));

        // Renaming is safe: the row holds the id, and shows the new name.
        var renamed = await s.Supervisor.PutAsJsonAsync($"/api/channels/{s.WhatsApp}",
            new UpsertChannelRequest($"{s.WhatsAppName} Business", 7));
        renamed.StatusCode.Should().Be(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());
        var dto = (await renamed.Content.ReadFromJsonAsync<ChannelDto>())!;
        dto.InUse.Should().BeTrue();
        dto.SortOrder.Should().Be(7);

        (await s.Supervisor.GetFromJsonAsync<List<CommunicationDto>>($"/api/communications/by-contact/{s.ContactId}"))!
            .First(c => c.Id == message).ChannelName.Should().Be($"{s.WhatsAppName} Business");

        // Hidden: gone from the agent's list, kept for the supervisor's, and refused for a new message.
        var hidden = await s.Supervisor.PutAsJsonAsync($"/api/channels/{s.Facebook}",
            new UpsertChannelRequest(s.FacebookName, 1, IsActive: false));
        hidden.StatusCode.Should().Be(HttpStatusCode.OK, await hidden.Content.ReadAsStringAsync());

        (await s.Agent.GetFromJsonAsync<List<ChannelDto>>("/api/channels"))!.Should().NotContain(c => c.Id == s.Facebook);
        (await s.Supervisor.GetFromJsonAsync<List<ChannelDto>>("/api/channels?includeInactive=true"))!
            .Should().Contain(c => c.Id == s.Facebook && !c.IsActive);

        var onHidden = await s.Agent.PostAsJsonAsync("/api/communications/applications",
            new RecordApplicationRequest(s.Facebook, s.CustomerMobile, null, null, null));
        onHidden.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(onHidden)).Should().Be("unknown_channel");

        // Phone is a system channel.
        var phoneRename = await s.Supervisor.PutAsJsonAsync($"/api/channels/{s.Phone}", new UpsertChannelRequest("Telephone"));
        phoneRename.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(phoneRename)).Should().Be("system_channel");

        var phoneHide = await s.Supervisor.PutAsJsonAsync($"/api/channels/{s.Phone}",
            new UpsertChannelRequest(ChannelNames.Phone, IsActive: false));
        phoneHide.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var phoneMove = await s.Supervisor.PutAsJsonAsync($"/api/channels/{s.Phone}", new UpsertChannelRequest(ChannelNames.Phone, 0));
        phoneMove.StatusCode.Should().Be(HttpStatusCode.OK, "moving it is fine");

        // Names are unique, whatever the case; agents do not manage the list.
        var duplicate = await s.Supervisor.PostAsJsonAsync("/api/channels", new UpsertChannelRequest(s.WhatsAppName.ToUpperInvariant() + " BUSINESS"));
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(duplicate)).Should().Be("bad_name");

        (await s.Agent.PostAsJsonAsync("/api/channels", new UpsertChannelRequest("Telegram"))).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- helpers --------------------------------------------------------------

    /// <summary>Sets <c>agent.edit_window</c> and returns what it was, or null when the row did not exist.</summary>
    private Task<string?> SetEditWindowAsync(string? value) => data.QueryAsync(async db =>
    {
        var row = await db.Settings.FirstOrDefaultAsync(x => x.Key == "agent.edit_window");
        var previous = row?.Value;

        if (value is null)
        {
            if (row is not null) db.Settings.Remove(row);
        }
        else if (row is null)
        {
            db.Settings.Add(new Setting { Key = "agent.edit_window", Value = value });
        }
        else
        {
            row.Value = value;
        }

        await db.SaveChangesAsync();
        return previous;
    });

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private sealed class Scenario(TestData data)
    {
        public required HttpClient Agent { get; init; }
        public required HttpClient Supervisor { get; init; }
        public required Guid AgentId { get; init; }
        public required string AgentName { get; init; }
        public required Guid ContactId { get; init; }
        public required string CustomerMobile { get; init; }
        public required Guid Phone { get; init; }
        public required Guid WhatsApp { get; init; }
        public required string WhatsAppName { get; init; }
        public required Guid Facebook { get; init; }
        public required string FacebookName { get; init; }
        public required Guid BranchId { get; init; }
        public required Guid OrderType { get; init; }
        public required Guid ComplaintType { get; init; }
        public required Guid CancellationType { get; init; }
        public required int FormVersion { get; init; }
        public required Guid AnsweredCall { get; init; }

        /// <summary>A complete classification, for a message a test records: one is always required.</summary>
        public SaveClassificationRequest Classified => new(ComplaintType, BranchId, null, null);

        /// <summary>The supervisor's search, always narrowed to this scenario's agent.</summary>
        public async Task<CallSearchPageDto> SearchAsync(string query)
        {
            var response = await Supervisor.GetAsync($"/api/communications/search?agentId={AgentId}&{query}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<CallSearchPageDto>())!;
        }

        /// <summary>One report, narrowed to this scenario's agent.</summary>
        public async Task<T> ReportAsync<T>(string report, string query = "")
        {
            var response = await Supervisor.GetAsync($"/api/reports/applications/{report}?agentId={AgentId}&{query}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<T>())!;
        }

        public Task<int> CountMessagesAsync() => data.QueryAsync(db =>
            db.Communications.CountAsync(c => c.AgentId == AgentId && c.Kind == CommunicationKinds.App));

        /// <summary>
        /// A message written straight to the table, as the recording endpoint
        /// writes it, so a test can place it on any day and any agent.
        /// </summary>
        public Task<Guid> MessageAsync(
            Guid channelId, DateTimeOffset at, Guid? typeId = null, decimal? orderValue = null, Guid? agentId = null) =>
            data.QueryAsync(async db =>
            {
                var message = new Communication
                {
                    Kind = CommunicationKinds.App,
                    Direction = Directions.None,
                    Status = CommunicationStatuses.Logged,
                    ChannelId = channelId,
                    AgentId = agentId ?? AgentId,
                    ContactId = ContactId,
                    BranchId = BranchId,
                    RemoteNumberRaw = CustomerMobile,
                    RemoteNormalised = PhoneNormalizer.Normalize(CustomerMobile),
                    StartedAt = at,
                    Source = CommunicationSources.Manual,
                };

                db.Communications.Add(message);
                await db.SaveChangesAsync();

                if (typeId is { } type)
                {
                    db.Classifications.Add(new Classification
                    {
                        CommunicationId = message.Id,
                        TypeId = type,
                        OrderValue = orderValue,
                        FormVersion = FormVersion,
                        CustomValues = JsonDocument.Parse("{}"),
                        ClassifiedBy = agentId ?? AgentId,
                        ClassifiedAt = at,
                    });
                    await db.SaveChangesAsync();
                }

                return message.Id;
            });
    }

    /// <summary>
    /// An agent with one answered, classified phone order from a known customer;
    /// two app channels of this run's own; and the Order, Complaint and
    /// Cancellation types, which the reports name.
    /// </summary>
    private async Task<Scenario> ScenarioAsync()
    {
        await data.EnsurePhoneChannelAsync();
        var agent = await data.CreateUserAsync();
        var (agentClient, _) = await data.SignInAsync(agent);
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        var mobile = TestData.NewMobile();
        var contact = await data.CreateContactAsync("سارة الحلبي", mobile);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;

        var ids = await data.QueryAsync(async db =>
        {
            var phone = await db.Channels.Where(c => c.Name == ChannelNames.Phone).Select(c => c.Id).FirstAsync();

            var whatsApp = new Channel { Name = $"WhatsApp {suffix}", SortOrder = 1 };
            var facebook = new Channel { Name = $"Facebook {suffix}", SortOrder = 2 };
            var branch = new Branch { Name = $"Test branch {suffix}" };
            db.AddRange(whatsApp, facebook, branch);

            // CI's database is migrated, not seeded, so the types the reports
            // are written against may not be there yet.
            async Task<Guid> TypeAsync(string name, string ar)
            {
                var existing = await db.ClassificationTypes.Where(t => t.Name == name).Select(t => (Guid?)t.Id).FirstOrDefaultAsync();
                if (existing is { } id) return id;
                var type = new ClassificationType { Name = name, LabelAr = ar, LabelEn = name, IsSystem = true };
                db.ClassificationTypes.Add(type);
                await db.SaveChangesAsync();
                return type.Id;
            }

            var order = await TypeAsync("Order", "طلب");
            var complaint = await TypeAsync("Complaint", "شكوى");
            var cancellation = await TypeAsync("Cancellation", "إلغاء");

            var form = new FormDefinition
            {
                Version = 100_000 + Random.Shared.Next(0, 1_000_000_000),
                Definition = JsonDocument.Parse("""{"fields":[]}"""),
            };
            db.Add(form);
            await db.SaveChangesAsync();

            var answered = new Communication
            {
                Kind = CommunicationKinds.Call,
                ChannelId = phone,
                Direction = Directions.In,
                Status = CommunicationStatuses.Answered,
                AgentId = agent.Id,
                ContactId = contact.Id,
                BranchId = branch.Id,
                RemoteNumberRaw = mobile,
                RemoteNormalised = PhoneNormalizer.Normalize(mobile),
                StartedAt = now.AddHours(-3),
                AnsweredAt = now.AddHours(-3).AddSeconds(5),
                EndedAt = now.AddHours(-3).AddMinutes(2),
                DurationSec = 115,
                Extension = "9100",
                SipCallId = TestData.NewSipCallId(),
                Source = CommunicationSources.AgentApp,
            };
            db.Communications.Add(answered);
            await db.SaveChangesAsync();

            db.Classifications.Add(new Classification
            {
                CommunicationId = answered.Id,
                TypeId = order,
                OrderValue = 30m,
                FormVersion = form.Version,
                CustomValues = JsonDocument.Parse("{}"),
                ClassifiedBy = agent.Id,
                ClassifiedAt = now,
            });
            await db.SaveChangesAsync();

            return (Phone: phone, WhatsApp: whatsApp.Id, WhatsAppName: whatsApp.Name, Facebook: facebook.Id,
                FacebookName: facebook.Name, Branch: branch.Id, Order: order, Complaint: complaint,
                Cancellation: cancellation, Form: form.Version, Answered: answered.Id);
        });

        return new Scenario(data)
        {
            Agent = agentClient,
            Supervisor = supervisor,
            AgentId = agent.Id,
            AgentName = agent.DisplayName,
            ContactId = contact.Id,
            CustomerMobile = mobile,
            Phone = ids.Phone,
            WhatsApp = ids.WhatsApp,
            WhatsAppName = ids.WhatsAppName,
            Facebook = ids.Facebook,
            FacebookName = ids.FacebookName,
            BranchId = ids.Branch,
            OrderType = ids.Order,
            ComplaintType = ids.Complaint,
            CancellationType = ids.Cancellation,
            FormVersion = ids.Form,
            AnsweredCall = ids.Answered,
        };
    }
}
