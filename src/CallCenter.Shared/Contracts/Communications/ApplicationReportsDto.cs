namespace CallCenter.Shared.Contracts.Communications;

/// <summary>
/// The application reports (A-72, R-12, R-13): figures about messages, never
/// calls. Every report takes S-07's common filters — period, agent, branch,
/// channel, type — and answers with rows a table draws as they are and a chart
/// draws where the figures are numeric (S-06).
/// </summary>
/// <remarks>
/// A count and a total on every row, in the same shape, so the page's table,
/// chart and CSV export are written once. What differs between reports is what
/// the rows are grouped by.
/// </remarks>

/// <summary>One type's share of a channel's messages.</summary>
public record TypeCountDto(string TypeName, string LabelAr, string LabelEn, int Count);

/// <summary>Messages on one channel, and what they were about.</summary>
public record ChannelReportRowDto(
    Guid ChannelId,
    string Channel,
    int Messages,
    int Unclassified,
    IReadOnlyList<TypeCountDto> ByType);

/// <summary>
/// Orders and their value under one heading: a channel, a branch, an agent or a
/// day, whichever the supervisor grouped by (R-12, R-13).
/// </summary>
/// <param name="Key">The id, or the day as <c>yyyy-MM-dd</c>, for a stable sort and the chart.</param>
/// <param name="Label">What to print: the channel's, branch's or agent's name, or the day.</param>
/// <param name="Average">Value per order. Null when there were none.</param>
public record OrdersReportRowDto(
    string Key,
    string Label,
    int Orders,
    decimal OrderValue,
    decimal? Average);

/// <summary>
/// One point of the trend (S-07's grouping): a day, a week, a month or an hour
/// of the day, with how many messages and orders fell in it.
/// </summary>
/// <param name="Bucket">
/// <c>yyyy-MM-dd</c> for a day or the Monday of a week, <c>yyyy-MM</c> for a
/// month, <c>HH</c> for an hour. In the restaurant's own time, as the edit
/// window is measured (A-42).
/// </param>
public record TrendPointDto(
    string Bucket,
    int Messages,
    int Orders,
    decimal OrderValue);

/// <summary>What one agent recorded, and what they still owe (A-71, R-15).</summary>
public record AgentReportRowDto(
    Guid AgentId,
    string Agent,
    int Messages,
    int Orders,
    decimal OrderValue,
    int Unclassified);

/// <summary>The messages that went wrong, per channel: cancellations and complaints (R-14, R-17).</summary>
public record IssuesReportRowDto(
    Guid ChannelId,
    string Channel,
    int Messages,
    int Cancellations,
    int Complaints);
