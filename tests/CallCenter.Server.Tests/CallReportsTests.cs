using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Reports;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using CallCenter.Shared.Phone;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The call reports (R-01 to R-18) and the dashboard (S-20), against a real
/// database and rows whose every figure is known.
/// </summary>
/// <remarks>
/// <b>The acceptance line, as a test</b> (SRS 8): "Reports R-01 to R-05 produce
/// correct figures and charts for a test day with known calls, filterable by
/// branch, and export to Excel." The day is a random one years back, so no
/// other test's rows fall on it, and every report is narrowed to one of this
/// run's own two branches, so no earlier run's can either.
/// </remarks>
[Collection(ApiCollection.Name)]
public class CallReportsTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    // ---- the acceptance test: R-01 to R-05, by branch, and the export -------------

    [DatabaseFact]
    public async Task A_known_day_gives_exact_figures_for_R01_to_R05_per_branch_and_the_export_holds_the_same_rows()
    {
        var d = await DayAsync();
        try
        {
            await CheckTheDayAsync(d);
        }
        finally
        {
            await SetInternalNumbersAsync(_ => d.PreviousInternalNumbers);
        }
    }

    private static async Task CheckTheDayAsync(KnownDay d)
    {
        // ---- branch A ----

        // R-01: 10 customer calls and 1 message; the internal call is left out (S-48).
        var summary = await d.GetAsync<List<CallSummaryRowDto>>("calls/summary", d.BranchA);
        var day = summary.Should().ContainSingle().Subject;
        day.Bucket.Should().Be(d.DayKey);
        day.Communications.Should().Be(11);
        day.Calls.Should().Be(10);
        day.Messages.Should().Be(1);
        day.Inbound.Should().Be(8);
        day.Outbound.Should().Be(2);
        day.Answered.Should().Be(5, "inbound answered: two orders, two complaints, one unclassified");
        day.Missed.Should().Be(2, "one Missed and one Rejected; the NoAnswer is an outbound call (A-21)");
        day.Blocked.Should().Be(1);

        // R-03: per type, over the five classified calls. The message is not a call.
        var byType = await d.GetAsync<List<TypeShareRowDto>>("calls/by-type", d.BranchA);
        byType.Select(r => (r.TypeName, r.Count, r.Share)).Should().BeEquivalentTo(
            [("Order", 2, 40.0m), ("Complaint", 2, 40.0m), ("Inquiry", 1, 20.0m)]);

        // R-04: per agent, and per day.
        var byAgent = await d.GetAsync<List<CallBreakdownRowDto>>("calls/breakdown", d.BranchA, "groupBy=agent");
        var one = byAgent.Single(r => r.Key == d.AgentOne.ToString());
        (one.Calls, one.Answered, one.Missed, one.Orders, one.OrderValue).Should().Be((6, 3, 1, 2, 80m));
        one.ByType.Select(t => (t.TypeName, t.Count)).Should().BeEquivalentTo([("Order", 2), ("Inquiry", 1)]);
        var two = byAgent.Single(r => r.Key == d.AgentTwo.ToString());
        (two.Calls, two.Answered, two.Missed, two.Orders, two.OrderValue).Should().Be((4, 3, 1, 0, 0m));
        byAgent.Should().HaveCount(2);

        var perDay = await d.GetAsync<List<CallBreakdownRowDto>>("calls/breakdown", d.BranchA, "groupBy=day");
        perDay.Should().ContainSingle().Which.Calls.Should().Be(10);

        // R-04: recurring customers, ranked. The unknown numbers are nobody's.
        var recurring = await d.GetAsync<List<CustomerRankRowDto>>("calls/recurring-customers", d.BranchA);
        recurring.Select(r => (r.ContactId, r.Calls, r.Orders, r.OrderValue)).Should().Equal(
            (d.CustomerY, 4, 0, 0m), (d.CustomerX, 3, 2, 80m));

        // R-05: the complaints, newest first, with notes and follow-up status.
        var complaints = await d.GetAsync<List<ProblemRowDto>>("calls/complaints", d.BranchA);
        complaints.Should().HaveCount(2);
        complaints[0].Resolved.Should().BeTrue();
        complaints[0].ResolvedAt.Should().NotBeNull();
        complaints[1].Notes.Should().Be("cold burger, again");
        complaints[1].FollowUp.Should().BeTrue();
        complaints[1].Resolved.Should().BeFalse();
        complaints.Should().OnlyContain(c => c.ContactId == d.CustomerY && c.Agent == d.AgentTwoName);

        // R-05 per branch and per agent, with R-17's handling figures.
        var perBranch = await d.GetAsync<List<ComplaintsRowDto>>("calls/complaints/by", d.BranchA, "groupBy=branch");
        var a = perBranch.Should().ContainSingle().Subject;
        (a.Complaints, a.FollowUp, a.Resolved, a.Open, a.AverageHoursToResolve, a.Orders, a.PerHundredOrders)
            .Should().Be((2, 1, 1, 1, 2.0m, 2, 100.0m));
        var perAgent = await d.GetAsync<List<ComplaintsRowDto>>("calls/complaints/by", d.BranchA, "groupBy=agent");
        perAgent.Should().ContainSingle().Which.Key.Should().Be(d.AgentTwo.ToString());
        perAgent[0].PerHundredOrders.Should().BeNull("agent two took no orders");

        // R-05: repeat complainers.
        var repeat = await d.GetAsync<List<CustomerRankRowDto>>("calls/repeat-complainers", d.BranchA);
        repeat.Should().ContainSingle().Which.ContactId.Should().Be(d.CustomerY);
        repeat[0].Complaints.Should().Be(2);

        // ---- branch B: the same reports, other figures ----

        var summaryB = (await d.GetAsync<List<CallSummaryRowDto>>("calls/summary", d.BranchB)).Single();
        (summaryB.Communications, summaryB.Calls, summaryB.Messages, summaryB.Answered, summaryB.Missed)
            .Should().Be((2, 2, 0, 1, 1));
        (await d.GetAsync<List<TypeShareRowDto>>("calls/by-type", d.BranchB))
            .Select(r => (r.TypeName, r.Count, r.Share)).Should().Equal(("Order", 1, 100.0m));
        (await d.GetAsync<List<ProblemRowDto>>("calls/complaints", d.BranchB)).Should().BeEmpty();

        // The day after is not in the day.
        var twoDays = await d.GetAsync<List<CallSummaryRowDto>>("calls/summary", d.BranchA, $"to={Instant(d.Day.AddDays(2))}");
        twoDays.Should().HaveCount(2);
        twoDays.Sum(r => r.Calls).Should().Be(11);

        // ---- R-02: the search, and its export holds the same rows ----

        var search = await d.Supervisor.GetFromJsonAsync<CallSearchPageDto>(
            $"/api/communications/search?branchId={d.BranchA}&from={Instant(d.Day)}&to={Instant(d.Day.AddDays(1))}&pageSize=100");
        search!.Total.Should().Be(11, "the list is every call, the internal one included: S-48 keeps it out of the figures, not the record");

        var export = await d.Supervisor.GetAsync(
            $"/api/communications/search/export?branchId={d.BranchA}&from={Instant(d.Day)}&to={Instant(d.Day.AddDays(1))}&lang=en");
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        export.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var bytes = await export.Content.ReadAsByteArrayAsync();
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).TrimEnd().Split("\r\n");
        lines[0].Should().Be("Date,Time,Direction,Result,Agent,Customer,Number,Branch,Channel,Type,Order value,Duration (seconds),Notes,Recording");
        lines.Should().HaveCount(1 + 11);
        var numbers = lines.Skip(1).Select(l => l.Split(',')[6]).ToList();
        numbers.Should().BeEquivalentTo(search.Rows.Select(r => r.RemoteNumberRaw));
        lines.Should().Contain(l => l.Contains("\"cold burger, again\""), "a comma in a note is quoted");
        lines.Skip(1).Should().OnlyContain(l => l.StartsWith(d.DayKey));

        var arabic = await d.Supervisor.GetByteArrayAsync(
            $"/api/communications/search/export?branchId={d.BranchB}&from={Instant(d.Day)}&to={Instant(d.Day.AddDays(1))}");
        var arabicText = Encoding.UTF8.GetString(arabic, 3, arabic.Length - 3);
        arabicText.Should().StartWith("التاريخ,الوقت,الاتجاه,النتيجة");
        arabicText.Should().Contain("فائتة").And.Contain("تم الرد");

        // ---- S-20's charts for the chosen period: everything on the day ----

        var period = await d.Supervisor.GetFromJsonAsync<DashboardPeriodDto>(
            $"/api/reports/dashboard/period?from={Instant(d.Day)}&to={Instant(d.Day.AddDays(1))}");
        period!.PerDay.Should().ContainSingle().Which.Should().Be(new CountDto(d.DayKey, d.DayKey, 13));
        period.PerHour.Should().HaveCount(24);
        period.PerHour.Sum(h => h.Count).Should().Be(13);
        period.PerHour.Single(h => h.Key == "19").Count.Should().Be(2);
        period.PerChannel.Single(c => c.Label == ChannelNames.Phone).Count.Should().Be(12);
        period.PerChannel.Single(c => c.Key == d.WhatsApp.ToString()).Count.Should().Be(1);
        period.PerType.Select(t => (t.TypeName, t.Count)).Should().BeEquivalentTo(
            [("Order", 4), ("Complaint", 2), ("Inquiry", 1)]);

        // Reports are the supervisor's (section 4).
        (await d.Agent.GetAsync($"/api/reports/calls/summary?branchId={d.BranchA}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await d.Agent.GetAsync($"/api/communications/search/export?branchId={d.BranchA}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await d.Agent.GetAsync("/api/reports/dashboard/today")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- S-20: today ------------------------------------------------------------

    [DatabaseFact]
    public async Task The_dashboard_counts_what_happened_today_and_the_agents_signed_in()
    {
        var d = await DayAsync(seedDay: false);
        var before = (await d.Supervisor.GetFromJsonAsync<DashboardTodayDto>("/api/reports/dashboard/today"))!;

        var now = DateTimeOffset.Now;
        var start = ReportDay(now);
        // Early in the day so every row is today whenever the test runs, and
        // never in the future.
        DateTimeOffset At(int minutes) => now - start > TimeSpan.FromMinutes(10) ? start.AddMinutes(minutes) : now.AddSeconds(-minutes);

        await d.CallAsync(d.BranchA, At(1), Directions.In, CommunicationStatuses.Answered, d.AgentOne, d.CustomerX, type: d.Order, value: 25m);
        await d.CallAsync(d.BranchA, At(2), Directions.In, CommunicationStatuses.Missed, d.AgentOne, null);
        await d.CallAsync(d.BranchA, At(3), Directions.In, CommunicationStatuses.Answered, d.AgentTwo, d.CustomerY);
        await d.CallAsync(d.BranchA, At(4), Directions.Out, CommunicationStatuses.NoAnswer, d.AgentTwo, d.CustomerY);
        await d.MessageAsync(d.BranchA, At(5), d.AgentTwo, d.Complaint);

        // Signing in to the Agent App opens a session: one more agent online.
        var third = await data.CreateUserAsync();
        await data.SignInAsync(third);

        var after = (await d.Supervisor.GetFromJsonAsync<DashboardTodayDto>("/api/reports/dashboard/today"))!;

        (after.Communications - before.Communications).Should().Be(5);
        (after.Calls - before.Calls).Should().Be(4);
        (after.Messages - before.Messages).Should().Be(1);
        (after.Orders - before.Orders).Should().Be(1);
        (after.OrderValue - before.OrderValue).Should().Be(25m);
        (after.Complaints - before.Complaints).Should().Be(1);
        (after.Missed - before.Missed).Should().Be(1, "the NoAnswer is not a missed call");
        (after.Unclassified - before.Unclassified).Should().Be(1);
        (after.AgentsOnline - before.AgentsOnline).Should().Be(1);
        after.ByChannel.Single(c => c.Key == d.WhatsApp.ToString()).Count.Should().Be(1);
    }

    // ---- Phase 2: R-10 to R-18, on the same known day ----------------------------

    [DatabaseFact]
    public async Task Phase_2_reports_give_exact_figures_on_the_known_day_including_each_definitions_edge()
    {
        var d = await DayAsync();
        try
        {
            await CheckPhaseTwoAsync(d);
        }
        finally
        {
            await SetInternalNumbersAsync(_ => d.PreviousInternalNumbers);
        }
    }

    private async Task CheckPhaseTwoAsync(KnownDay d)
    {
        var a = d.BranchA;
        var twoDays = $"to={Instant(d.Day.AddDays(2))}";

        // R-10: incoming calls only, all 24 hours, Monday first.
        var peak = await d.GetAsync<List<PeakHourRowDto>>("calls/peak-hours", a);
        peak.Should().HaveCount(24);
        peak.Sum(h => h.Total).Should().Be(8, "the two outgoing calls are not demand, and the internal call is left out");
        var weekday = ((int)d.Day.DayOfWeek + 6) % 7;
        peak[19].ByWeekday[weekday].Should().Be(2);
        peak[21].ByWeekday[weekday].Should().Be(2);
        peak[20].Total.Should().Be(0, "20:00 and 20:30 were outgoing");

        // R-11: missed and rejected, and their share of incoming. NoAnswer is not missed (A-21).
        var missed = (await d.GetAsync<List<MissedRowDto>>("calls/missed", a, "groupBy=day")).Single();
        (missed.Inbound, missed.Missed, missed.Rejected, missed.Total, missed.Rate).Should().Be((8, 1, 1, 2, 25.0m));
        var missedByAgent = await d.GetAsync<List<MissedRowDto>>("calls/missed", a, "groupBy=agent");
        missedByAgent.Single(r => r.Key == d.AgentOne.ToString()).Missed.Should().Be(1);
        missedByAgent.Single(r => r.Key == d.AgentTwo.ToString()).Rejected.Should().Be(1);
        var missedList = await d.GetAsync<List<MissedCallRowDto>>("calls/missed/list", a);
        missedList.Select(r => r.Status).Should().Equal(CommunicationStatuses.Rejected, CommunicationStatuses.Missed);
        missedList[0].ContactId.Should().Be(d.CustomerX);

        // R-12: phone against the apps.
        var byChannel = await d.GetAsync<List<ChannelOrdersRowDto>>("calls/orders-by-channel", a);
        byChannel.Select(r => (r.ChannelId, r.Orders, r.OrderValue, r.Share)).Should().Equal(
            (d.Phone, 2, 80m, 66.7m), (d.WhatsApp, 1, 40m, 33.3m));
        var trend = (await d.GetAsync<List<ChannelOrdersTrendPointDto>>("calls/orders-trend", a, "groupBy=day")).Single();
        (trend.PhoneOrders, trend.AppOrders, trend.PhoneValue, trend.AppValue).Should().Be((2, 1, 80m, 40m));

        // R-13, over both days: the cancellation's 30 is not revenue.
        var value = await d.GetAsync<List<OrdersReportRowDto>>("calls/orders", a, $"groupBy=channel&{twoDays}");
        value.Single(r => r.Key == d.Phone.ToString()).OrderValue.Should().Be(80m);
        value.Single(r => r.Key == d.Phone.ToString()).Average.Should().Be(40m);
        value.Sum(r => r.Orders).Should().Be(3);

        // R-14, over both days.
        var perBranch = (await d.GetAsync<List<CancellationRateRowDto>>("calls/cancellations", a, $"groupBy=branch&{twoDays}")).Single();
        (perBranch.Orders, perBranch.Cancellations, perBranch.Rate).Should().Be((3, 1, 33.3m));
        var perChannel = await d.GetAsync<List<CancellationRateRowDto>>("calls/cancellations", a, $"groupBy=channel&{twoDays}");
        perChannel.Single(r => r.Key == d.Phone.ToString()).Rate.Should().Be(50.0m);
        perChannel.Single(r => r.Key == d.WhatsApp.ToString()).Rate.Should().Be(0m);
        var cancellations = await d.GetAsync<List<ProblemRowDto>>("calls/cancellations/list", a, twoDays);
        cancellations.Should().ContainSingle().Which.Notes.Should().Be("changed their mind");

        // R-15: talk time averaged over answered calls only.
        var agents = await d.GetAsync<List<AgentProductivityRowDto>>("calls/agents", a);
        var one = agents.Single(r => r.AgentId == d.AgentOne);
        (one.Handled, one.Inbound, one.Outbound, one.AverageDurationSec, one.Orders, one.OrderValue, one.Unclassified, one.Missed)
            .Should().Be((3, 2, 2, 115, 2, 80m, 0, 1));
        var two = agents.Single(r => r.AgentId == d.AgentTwo);
        (two.Handled, two.Inbound, two.Outbound, two.AverageDurationSec, two.Orders, two.Unclassified, two.Missed)
            .Should().Be((3, 3, 0, 115, 0, 1, 1));

        // R-16: two customers called — the unknown numbers are nobody — X returning, Y new.
        var customers = (await d.GetAsync<List<CustomerBaseRowDto>>("calls/customers", a, "groupBy=day")).Single();
        (customers.Bucket, customers.Customers, customers.New, customers.Returning).Should().Be((d.DayKey, 2, 1, 1));
        var carriedOver = (await d.GetAsync<List<CustomerBaseRowDto>>("calls/customers", d.BranchB, "groupBy=day")).Single();
        (carriedOver.Customers, carriedOver.New).Should().Be((1, 0), "a contact nobody here saved came with the old system's book");
        var top = await d.GetAsync<List<CustomerRankRowDto>>("calls/top-customers", a, "by=value");
        top.Should().ContainSingle().Which.Should().Match<CustomerRankRowDto>(c => c.ContactId == d.CustomerX && c.Orders == 2 && c.OrderValue == 80m);
        // Inactive is measured back from today over every order: X last ordered years ago.
        var inactive = await d.Supervisor.GetFromJsonAsync<List<CustomerRankRowDto>>(
            $"/api/reports/calls/inactive-customers?branchId={a}&days=30");
        inactive.Should().ContainSingle().Which.ContactId.Should().Be(d.CustomerX, "Y never ordered, and a number nobody saved is not a customer");

        // R-18.
        var quality = await d.GetAsync<DataQualityDto>("calls/data-quality", a);
        (quality.Unclassified, quality.UnknownCalls, quality.UnknownNumbers).Should().Be((1, 3, 3));
        var unknown = await d.GetAsync<List<UnknownNumberRowDto>>("calls/unknown-numbers", a);
        unknown.Should().HaveCount(3).And.OnlyContain(r => r.Calls == 1);

        // Duplicate names: the same name however it is spelt (A-80), never the same number.
        var twin = $"سارة {Guid.NewGuid():N}"[..14];
        var first = await data.CreateContactAsync(twin, TestData.NewMobile());
        var second = await data.CreateContactAsync(twin.Replace('ة', 'ه'), TestData.NewMobile());
        await data.QueryAsync(async db =>
        {
            foreach (var c in await db.Contacts.Where(c => c.Id == first.Id || c.Id == second.Id).ToListAsync())
            {
                c.NameNormalised = CallCenter.Shared.Text.NameNormalizer.Normalize(c.Name);
            }

            return await db.SaveChangesAsync();
        });
        var duplicates = await d.Supervisor.GetFromJsonAsync<List<DuplicateNameRowDto>>("/api/reports/calls/duplicate-names");
        var pair = duplicates!.Should().ContainSingle(r => r.Name == twin || r.Name == twin.Replace('ة', 'ه')).Subject;
        pair.Contacts.Should().Be(2);
        (await d.GetAsync<DataQualityDto>("calls/data-quality", a)).DuplicateNames.Should().BeGreaterThan(0);
    }

    // ---- the restaurant's clock ------------------------------------------------------

    [Fact]
    public void The_period_is_cut_where_the_clocks_change_and_each_piece_has_its_own_offset()
    {
        var from = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddYears(1);
        var segments = ReportCube.Segments(from, to);

        segments[0].From.Should().Be(from);
        segments[^1].To.Should().Be(to);
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            if (i > 0) s.From.Should().Be(segments[i - 1].To, "the pieces join without a gap");
            ((int)TimeZoneInfo.Local.GetUtcOffset(s.From).TotalMinutes).Should().Be(s.OffsetMinutes);
            ((int)TimeZoneInfo.Local.GetUtcOffset(s.To.AddMinutes(-1)).TotalMinutes).Should().Be(s.OffsetMinutes);
        }
    }

    // ---- helpers -------------------------------------------------------------------

    private static string Instant(DateTimeOffset at) => Uri.EscapeDataString(at.ToString("o", CultureInfo.InvariantCulture));

    private static DateTimeOffset ReportDay(DateTimeOffset now)
    {
        var local = now.ToLocalTime();
        return new DateTimeOffset(local.Date, local.Offset);
    }

    private sealed class KnownDay(TestData data)
    {
        public required HttpClient Supervisor { get; init; }
        public required HttpClient Agent { get; init; }
        public required DateTimeOffset Day { get; init; }
        public required string DayKey { get; init; }
        public required Guid BranchA { get; init; }
        public required Guid BranchB { get; init; }
        public required Guid AgentOne { get; init; }
        public required Guid AgentTwo { get; init; }
        public required string AgentTwoName { get; init; }
        public required Guid CustomerX { get; init; }
        public required Guid CustomerY { get; init; }
        public required Guid CustomerZ { get; init; }
        public required Guid Phone { get; init; }
        public required Guid WhatsApp { get; init; }
        public required Guid Order { get; init; }
        public required Guid Complaint { get; init; }
        public required Guid Inquiry { get; init; }
        public required Guid Cancellation { get; init; }
        public required int FormVersion { get; init; }

        /// <summary>What <c>reports.internal_numbers</c> held before the day added one, to put back.</summary>
        public string? PreviousInternalNumbers { get; set; }

        public DateTimeOffset At(int hour, int minute = 0) => Day.AddHours(hour).AddMinutes(minute);

        /// <summary>A report for one branch over the day, unless the query says otherwise.</summary>
        public async Task<T> GetAsync<T>(string report, Guid branch, string query = "")
        {
            var period = query.Contains("to=") ? $"from={Instant(Day)}" : $"from={Instant(Day)}&to={Instant(Day.AddDays(1))}";
            var response = await Supervisor.GetAsync($"/api/reports/{report}?branchId={branch}&{period}&{query}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<T>())!;
        }

        /// <summary>A call written straight to the table, as the Agent App's report writes it (A-14).</summary>
        public Task<Guid> CallAsync(
            Guid branch, DateTimeOffset at, string direction, string status, Guid? agent, Guid? contact,
            string? number = null, Guid? type = null, decimal? value = null, string? notes = null,
            bool followUp = false, TimeSpan? resolvedAfter = null) =>
            data.QueryAsync(async db =>
            {
                number ??= contact is { } c
                    ? await db.ContactPhones.Where(p => p.ContactId == c).Select(p => p.Raw).FirstAsync()
                    : TestData.NewMobile();
                var answered = status == CommunicationStatuses.Answered;

                var call = new Communication
                {
                    Kind = CommunicationKinds.Call,
                    ChannelId = Phone,
                    Direction = direction,
                    Status = status,
                    AgentId = agent,
                    ContactId = contact,
                    BranchId = branch,
                    RemoteNumberRaw = number,
                    RemoteNormalised = PhoneNormalizer.Normalize(number),
                    StartedAt = at,
                    AnsweredAt = answered ? at.AddSeconds(5) : null,
                    EndedAt = at.AddMinutes(2),
                    DurationSec = answered ? 115 : null,
                    SipCallId = TestData.NewSipCallId(),
                    Source = CommunicationSources.AgentApp,
                };
                db.Communications.Add(call);
                await db.SaveChangesAsync();

                if (type is { } t)
                {
                    await ClassifyAsync(db, call.Id, t, value, notes, followUp, agent ?? AgentOne, at, resolvedAfter);
                }

                return call.Id;
            });

        /// <summary>A message (A-70), on this run's WhatsApp.</summary>
        public Task<Guid> MessageAsync(Guid branch, DateTimeOffset at, Guid agent, Guid type, decimal? value = null) =>
            data.QueryAsync(async db =>
            {
                var number = TestData.NewMobile();
                var message = new Communication
                {
                    Kind = CommunicationKinds.App,
                    ChannelId = WhatsApp,
                    Direction = Directions.None,
                    Status = CommunicationStatuses.Logged,
                    AgentId = agent,
                    BranchId = branch,
                    RemoteNumberRaw = number,
                    RemoteNormalised = PhoneNormalizer.Normalize(number),
                    StartedAt = at,
                    Source = CommunicationSources.Manual,
                };
                db.Communications.Add(message);
                await db.SaveChangesAsync();
                await ClassifyAsync(db, message.Id, type, value, null, false, agent, at, null);
                return message.Id;
            });

        private async Task ClassifyAsync(
            Server.Data.CallCenterDbContext db, Guid id, Guid type, decimal? value, string? notes, bool followUp,
            Guid by, DateTimeOffset at, TimeSpan? resolvedAfter)
        {
            db.Classifications.Add(new Classification
            {
                CommunicationId = id,
                TypeId = type,
                OrderValue = value,
                Notes = notes,
                FollowUp = followUp,
                Resolved = resolvedAfter is null ? null : true,
                ResolvedAt = resolvedAfter is { } after ? at + after : null,
                FormVersion = FormVersion,
                CustomValues = JsonDocument.Parse("{}"),
                ClassifiedBy = by,
                ClassifiedAt = at,
            });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Two agents, two branches, three customers, a WhatsApp of this run's own,
    /// and — unless <paramref name="seedDay"/> is false — one known day:
    /// <code>
    /// branch A                                     branch B
    ///  09:10 in  answered  one  X  Order 50         10:00 in answered one Z Order 100
    ///  12:00 in  answered  one  X  Order 30         11:00 in missed   two
    ///  13:00 in  answered  two  Y  Complaint, follow-up, open
    ///  15:00 WhatsApp      one     Order 40
    ///  18:00 in  answered  two  Y  Complaint, resolved 2 h later
    ///  19:00 in  missed    one  (unknown number)
    ///  19:30 in  rejected  two  X
    ///  20:00 out noanswer  one  Y
    ///  20:30 out answered  one  Y  Inquiry
    ///  21:00 in  answered  two  (unknown), unclassified
    ///  21:30 in  blocked   one  (unknown)
    ///  21:45 in  answered  one  internal number (S-48)
    ///  next day 10:00 in answered one X  Cancellation, value 30 (not revenue)
    /// X was saved by an agent a month before the day, Y by an agent since it,
    /// and Z by nobody here (as the old system's customers were).
    /// </code>
    /// </summary>
    private async Task<KnownDay> DayAsync(bool seedDay = true)
    {
        await data.EnsurePhoneChannelAsync();
        var one = await data.CreateUserAsync();
        var two = await data.CreateUserAsync();
        var (agentClient, _) = await data.SignInAsync(one);
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        var x = await data.CreateContactAsync("خالد الزبون", TestData.NewMobile());
        var y = await data.CreateContactAsync("يوسف المشتكي", TestData.NewMobile());
        var z = await data.CreateContactAsync("زياد", TestData.NewMobile());
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // A day years back, different on every run, at the restaurant's midnight.
        var date = new DateTime(2019, 1, 1).AddDays(Random.Shared.Next(0, 2500));
        var dayStart = new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));

        var ids = await data.QueryAsync(async db =>
        {
            var phone = await db.Channels.Where(c => c.Name == ChannelNames.Phone).Select(c => c.Id).FirstAsync();
            var whatsApp = new Channel { Name = $"WhatsApp {suffix}", SortOrder = 1 };
            var a = new Branch { Name = $"Test branch {suffix}" };
            var b = new Branch { Name = $"Test branch {Guid.NewGuid().ToString("N")[..8]}" };
            db.AddRange(whatsApp, a, b);

            // CI's database is migrated, not seeded.
            async Task<Guid> TypeAsync(string name, string ar)
            {
                var existing = await db.ClassificationTypes.Where(t => t.Name == name).Select(t => (Guid?)t.Id).FirstOrDefaultAsync();
                if (existing is { } id) return id;
                var type = new ClassificationType { Name = name, LabelAr = ar, LabelEn = name };
                db.ClassificationTypes.Add(type);
                await db.SaveChangesAsync();
                return type.Id;
            }

            var order = await TypeAsync("Order", "طلب");
            var complaint = await TypeAsync("Complaint", "شكوى");
            var inquiry = await TypeAsync("Inquiry", "استفسار");
            var cancellation = await TypeAsync("Cancellation", "إلغاء");

            var form = new FormDefinition
            {
                Version = 100_000 + Random.Shared.Next(0, 1_000_000_000),
                Definition = JsonDocument.Parse("""{"fields":[]}"""),
            };
            db.Add(form);
            await db.SaveChangesAsync();

            return (Phone: phone, WhatsApp: whatsApp.Id, A: a.Id, B: b.Id, Order: order, Complaint: complaint,
                Inquiry: inquiry, Cancellation: cancellation, Form: form.Version);
        });

        var day = new KnownDay(data)
        {
            Supervisor = supervisor,
            Agent = agentClient,
            Day = dayStart,
            DayKey = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            BranchA = ids.A,
            BranchB = ids.B,
            AgentOne = one.Id,
            AgentTwo = two.Id,
            AgentTwoName = two.DisplayName,
            CustomerX = x.Id,
            CustomerY = y.Id,
            CustomerZ = z.Id,
            Phone = ids.Phone,
            WhatsApp = ids.WhatsApp,
            Order = ids.Order,
            Complaint = ids.Complaint,
            Inquiry = ids.Inquiry,
            Cancellation = ids.Cancellation,
            FormVersion = ids.Form,
        };

        if (!seedDay) return day;

        const string In = Directions.In, Out = Directions.Out;
        const string Answered = CommunicationStatuses.Answered;
        var (a, b) = (day.BranchA, day.BranchB);

        await day.CallAsync(a, day.At(9, 10), In, Answered, one.Id, x.Id, type: day.Order, value: 50m);
        await day.CallAsync(a, day.At(12), In, Answered, one.Id, x.Id, type: day.Order, value: 30m);
        await day.CallAsync(a, day.At(13), In, Answered, two.Id, y.Id, type: day.Complaint, notes: "cold burger, again", followUp: true);
        await day.MessageAsync(a, day.At(15), one.Id, day.Order, 40m);
        await day.CallAsync(a, day.At(18), In, Answered, two.Id, y.Id, type: day.Complaint, resolvedAfter: TimeSpan.FromHours(2));
        await day.CallAsync(a, day.At(19), In, CommunicationStatuses.Missed, one.Id, null);
        await day.CallAsync(a, day.At(19, 30), In, CommunicationStatuses.Rejected, two.Id, x.Id);
        await day.CallAsync(a, day.At(20), Out, CommunicationStatuses.NoAnswer, one.Id, y.Id);
        await day.CallAsync(a, day.At(20, 30), Out, Answered, one.Id, y.Id, type: day.Inquiry);
        await day.CallAsync(a, day.At(21), In, Answered, two.Id, null);
        await day.CallAsync(a, day.At(21, 30), In, CommunicationStatuses.Blocked, one.Id, null);

        // S-48: an internal number, added to the list for this test and put back.
        var internalNumber = $"7{Random.Shared.Next(1000, 9999)}";
        var previous = await SetInternalNumbersAsync(n => string.IsNullOrWhiteSpace(n) ? internalNumber : $"{n},{internalNumber}");
        try
        {
            await day.CallAsync(a, day.At(21, 45), In, Answered, one.Id, null, number: internalNumber, type: day.Order, value: 999m);
        }
        catch
        {
            await SetInternalNumbersAsync(_ => previous);
            throw;
        }

        day.PreviousInternalNumbers = previous;

        // The next day: a cancellation with a value typed on it, which is not revenue.
        await day.CallAsync(a, day.At(24 + 10), In, Answered, one.Id, x.Id, type: day.Cancellation, value: 30m, notes: "changed their mind");

        // R-16: X was saved by an agent a month before the day (returning);
        // Y was saved by an agent now, after the day (new, A-11 takes the
        // earlier calls along); Z was saved by nobody here, as the old
        // system's customers were (returning, though saved now).
        await data.QueryAsync(async db =>
        {
            var saved = await db.Contacts.Where(c => c.Id == x.Id || c.Id == y.Id).ToListAsync();
            saved.Single(c => c.Id == x.Id).CreatedBy = one.Id;
            saved.Single(c => c.Id == x.Id).CreatedAt = dayStart.AddDays(-30);
            saved.Single(c => c.Id == y.Id).CreatedBy = two.Id;
            return await db.SaveChangesAsync();
        });

        await day.CallAsync(b, day.At(10), In, Answered, one.Id, z.Id, type: day.Order, value: 100m);
        await day.CallAsync(b, day.At(11), In, CommunicationStatuses.Missed, two.Id, null);

        return day;
    }

    /// <summary>Changes <c>reports.internal_numbers</c> and returns what it was.</summary>
    private Task<string?> SetInternalNumbersAsync(Func<string?, string?> change) => data.QueryAsync(async db =>
    {
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == "reports.internal_numbers");
        var previous = row?.Value;
        var value = change(previous) ?? string.Empty;
        if (row is null) db.Settings.Add(new Setting { Key = "reports.internal_numbers", Value = value });
        else row.Value = value;
        await db.SaveChangesAsync();
        return previous;
    });
}
