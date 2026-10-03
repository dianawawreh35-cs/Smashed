namespace CallCenter.Shared.Contracts.Mistakes;

/// <summary>Mistakes per branch (R-23): how many were the branch's own and how many its agents'.</summary>
/// <param name="BranchOwn">Put down to the branch as a whole.</param>
/// <param name="ByAgents">Put down to one of the agents, at this branch.</param>
/// <param name="Value">The value of all of them; a mistake with no value counts as nothing.</param>
/// <param name="Compensated">How many of them the customer has been compensated for (Dia, 3 Oct 2026).</param>
/// <param name="CompensatedValue">The value of those compensated.</param>
public record MistakeBranchRowDto(
    Guid BranchId,
    string Branch,
    int Mistakes,
    int BranchOwn,
    int ByAgents,
    decimal Value,
    int Compensated,
    decimal CompensatedValue);

/// <summary>Mistakes per agent (R-23): only those put down to an agent.</summary>
public record MistakeAgentRowDto(
    Guid AgentId,
    string Agent,
    int Mistakes,
    decimal Value,
    int Compensated,
    decimal CompensatedValue);

/// <summary>Mistakes over time (R-23).</summary>
/// <param name="Bucket"><c>yyyy-MM-dd</c> for a day or a week's Monday, <c>yyyy-MM</c> for a month.</param>
public record MistakeTrendPointDto(
    string Bucket,
    int Mistakes,
    int BranchOwn,
    int ByAgents,
    decimal Value,
    int Compensated,
    decimal CompensatedValue);

/// <summary>A customer who had more than one mistake in the period (R-23).</summary>
/// <param name="ContactId">The saved customer, or null for a number nobody has on file.</param>
/// <param name="Customer">The saved customer's name, or null.</param>
/// <param name="Number">The number as typed on their latest mistake.</param>
/// <param name="Compensated">How many of their mistakes they have been compensated for.</param>
/// <param name="Last">The day of their latest mistake.</param>
public record MistakeCustomerRowDto(
    Guid? ContactId,
    string? Customer,
    string Number,
    int Mistakes,
    decimal Value,
    int Compensated,
    DateOnly Last);
