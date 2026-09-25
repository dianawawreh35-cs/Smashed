namespace CallCenter.Shared.Contracts.Communications;

/// <summary>
/// The call reports (R-01 to R-18) and the dashboard (S-20). Calls only,
/// except where a report's point is comparing the phone with the apps (R-01's
/// calls vs app, R-12 to R-14, the dashboard's by-channel figures; Dia, 25 Sep).
/// </summary>
/// <remarks>
/// <b>The words, as every report here uses them</b> (docs/DECISIONS.md, 26 Sep):
/// <list type="bullet">
/// <item><b>Missed</b>: an inbound call with status Missed or Rejected, every
/// row. Never NoAnswer, which is an outbound call the customer did not pick up
/// (A-21).</item>
/// <item><b>Answered</b>: status Answered. <b>Duration</b> is talk time,
/// answered to ended, and an average is over answered calls only.</item>
/// <item><b>An order</b>: classified as type Order; its value is the
/// classification's order value.</item>
/// <item><b>Unclassified</b>: an answered call with no classification (A-41).</item>
/// <item><b>A customer</b>: a contact. A call from a number nobody saved is not
/// a customer's.</item>
/// <item><b>Internal calls</b> (S-48) are left out of every figure.</item>
/// </list>
/// </remarks>

/// <summary>
/// R-01: how many, in one period bucket. Communications = calls + messages.
/// Inbound = answered + missed + blocked (and any call still ringing).
/// </summary>
/// <param name="Answered">Inbound calls answered.</param>
/// <param name="Missed">Inbound calls Missed or Rejected.</param>
public record CallSummaryRowDto(
    string Bucket,
    int Communications,
    int Calls,
    int Messages,
    int Inbound,
    int Outbound,
    int Answered,
    int Missed,
    int Blocked);

/// <summary>R-03: one type's calls and its share of the classified calls, as a percentage to one decimal.</summary>
public record TypeShareRowDto(string TypeName, string LabelAr, string LabelEn, int Count, decimal Share);

/// <summary>
/// R-04: calls under one heading — a day, week, month, agent or branch — with
/// what they were about.
/// </summary>
/// <param name="Key">The id, or the bucket as the trend writes it, for a stable sort.</param>
public record CallBreakdownRowDto(
    string Key,
    string Label,
    int Calls,
    int Answered,
    int Missed,
    int Orders,
    decimal OrderValue,
    IReadOnlyList<TypeCountDto> ByType);

/// <summary>
/// One customer, ranked: R-04's recurring customers, R-05's repeat
/// complainers, R-16's top and inactive customers.
/// </summary>
/// <param name="LastAt">The last call in the period, or for the inactive list the last order ever.</param>
public record CustomerRankRowDto(
    Guid ContactId,
    string? Name,
    string? Number,
    int Calls,
    int Orders,
    decimal OrderValue,
    int Complaints,
    DateTimeOffset LastAt);

/// <summary>
/// One complaint or cancellation, for the lists in R-05 and R-14.
/// Follow-up status is the classification's own: <paramref name="FollowUp"/>
/// ticked by the agent, <paramref name="Resolved"/> by a supervisor.
/// </summary>
public record ProblemRowDto(
    Guid Id,
    DateTimeOffset StartedAt,
    string Channel,
    Guid? ContactId,
    string? Customer,
    string? Number,
    string? Agent,
    string? Branch,
    string? Notes,
    bool FollowUp,
    bool Resolved,
    DateTimeOffset? ResolvedAt);

/// <summary>
/// R-05 per branch or agent, and R-17: how many complaints, and how they were
/// handled. Open is a complaint not yet marked resolved.
/// </summary>
/// <param name="AverageHoursToResolve">From the call to its resolution, over the resolved ones. Null when none were.</param>
/// <param name="PerHundredOrders">Complaints per 100 orders under the same heading. Null when there were no orders.</param>
public record ComplaintsRowDto(
    string Key,
    string Label,
    int Complaints,
    int FollowUp,
    int Resolved,
    int Open,
    decimal? AverageHoursToResolve,
    int Orders,
    decimal? PerHundredOrders);

/// <summary>R-10: inbound calls in one hour of the day, per weekday, Monday first.</summary>
public record PeakHourRowDto(int Hour, IReadOnlyList<int> ByWeekday, int Total);

/// <summary>R-11: missed calls under one heading, and as a share of inbound calls.</summary>
/// <param name="Rate">Missed ÷ inbound, as a percentage to one decimal. Null when nothing came in.</param>
public record MissedRowDto(
    string Key,
    string Label,
    int Inbound,
    int Missed,
    int Rejected,
    int Total,
    decimal? Rate);

/// <summary>One missed call, for R-11's list.</summary>
public record MissedCallRowDto(
    Guid Id,
    DateTimeOffset StartedAt,
    string Status,
    string? Number,
    Guid? ContactId,
    string? Customer,
    string? Agent,
    string? Branch,
    string? Notes);

/// <summary>R-12: orders on one channel, and its share of all orders.</summary>
public record ChannelOrdersRowDto(Guid ChannelId, string Channel, int Orders, decimal OrderValue, decimal Share);

/// <summary>R-12's trend: orders by phone and through the apps, per bucket.</summary>
public record ChannelOrdersTrendPointDto(
    string Bucket,
    int PhoneOrders,
    int AppOrders,
    decimal PhoneValue,
    decimal AppValue);

/// <summary>R-14: cancellations against orders under one heading.</summary>
/// <param name="Rate">Cancellations ÷ orders, as a percentage to one decimal. Null when there were no orders.</param>
public record CancellationRateRowDto(string Key, string Label, int Orders, int Cancellations, decimal? Rate);

/// <summary>R-15: what one agent did with their calls.</summary>
/// <param name="Handled">Calls answered, in and out.</param>
/// <param name="AverageDurationSec">Talk time over answered calls. Null when none.</param>
/// <param name="Missed">Inbound calls on their extension Missed or Rejected.</param>
public record AgentProductivityRowDto(
    Guid AgentId,
    string Agent,
    int Handled,
    int Inbound,
    int Outbound,
    int? AverageDurationSec,
    int Orders,
    decimal OrderValue,
    int Unclassified,
    int Missed);

/// <summary>
/// R-16: customers who called in one bucket. New means somebody here saved the
/// contact in that bucket; returning means it existed before. The customers
/// carried over from the old system (nobody here saved them) are always
/// returning, even in the month the system went live.
/// </summary>
public record CustomerBaseRowDto(string Bucket, int Customers, int New, int Returning);

/// <summary>R-18's figures for the period, and the duplicate names, which have no period.</summary>
/// <param name="UnknownCalls">Calls with a number no contact holds.</param>
/// <param name="UnknownNumbers">How many different such numbers.</param>
/// <param name="DuplicateNames">Names shared by more than one contact.</param>
public record DataQualityDto(int Unclassified, int UnknownCalls, int UnknownNumbers, int DuplicateNames);

/// <summary>A number that rang or was rung and is not saved as anybody (R-18).</summary>
public record UnknownNumberRowDto(string Number, int Calls, DateTimeOffset FirstAt, DateTimeOffset LastAt);

/// <summary>Contacts sharing a name, normalised as the name search normalises it (R-18).</summary>
public record DuplicateNameRowDto(string Name, int Contacts, string Numbers);

/// <summary>A count in one bucket or category, for the dashboard's charts.</summary>
public record CountDto(string Key, string Label, int Count);

/// <summary>S-20's figures for the restaurant's today.</summary>
/// <param name="Communications">Calls and messages.</param>
/// <param name="Missed">Inbound calls Missed or Rejected.</param>
/// <param name="Unclassified">Answered calls with no classification.</param>
/// <param name="AgentsOnline">
/// Agents with a session not signed out (Dia, 25 Sep). Closing the app does not
/// sign it out, so this counts an agent who went home without signing out.
/// </param>
public record DashboardTodayDto(
    int Communications,
    int Calls,
    int Messages,
    IReadOnlyList<TypeCountDto> ByType,
    IReadOnlyList<CountDto> ByChannel,
    int Orders,
    decimal OrderValue,
    int Complaints,
    int Missed,
    int Unclassified,
    int AgentsOnline);

/// <summary>S-20's four charts for the chosen period: calls and messages together.</summary>
public record DashboardPeriodDto(
    IReadOnlyList<CountDto> PerDay,
    IReadOnlyList<TypeCountDto> PerType,
    IReadOnlyList<CountDto> PerChannel,
    IReadOnlyList<CountDto> PerHour);
