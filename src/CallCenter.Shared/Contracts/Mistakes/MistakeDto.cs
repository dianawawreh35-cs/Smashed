using System.ComponentModel.DataAnnotations;

namespace CallCenter.Shared.Contracts.Mistakes;

/// <summary>
/// One mistake the supervisor recorded against a branch or an agent (S-65).
/// </summary>
/// <param name="OccurredOn">The restaurant's day it happened on. A day, not an instant: nobody knows the minute.</param>
/// <param name="Responsible">
/// One of <see cref="MistakeResponsibilities"/>. <c>Branch</c> names no agent;
/// <c>Agent</c> always names one. The branch is there either way.
/// </param>
/// <param name="Value">What it cost, in shekels, when it cost something. Null when it had no value.</param>
/// <param name="Compensated">The customer has been compensated for it (تم التعويض; Dia, 3 Oct 2026).</param>
/// <param name="ContactId">The saved customer the number belonged to when the mistake was saved, if any.</param>
/// <param name="ContactName">That customer's name as it is now.</param>
/// <param name="CustomerNumber">The customer's number as the supervisor typed it. Kept when nobody has it on file.</param>
public record MistakeDto(
    Guid Id,
    DateOnly OccurredOn,
    Guid BranchId,
    string BranchName,
    string Responsible,
    Guid? AgentId,
    string? AgentDisplayName,
    decimal? Value,
    bool Compensated,
    Guid? ContactId,
    string? ContactName,
    string? CustomerNumber,
    string Notes,
    string? CreatedByDisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A page of the mistakes matching a search, with the count and the value of all of them.</summary>
/// <param name="TotalValue">The sum of <see cref="MistakeDto.Value"/> over every match, not only this page.</param>
public record MistakePageDto(
    IReadOnlyList<MistakeDto> Rows,
    int Total,
    decimal TotalValue,
    int Page,
    int PageSize);

/// <summary>Records a mistake, or corrects one (S-65).</summary>
/// <param name="AgentId">Required when <paramref name="Responsible"/> is <c>Agent</c>; must be empty when it is <c>Branch</c>.</param>
/// <param name="CustomerNumber">Optional. Any format: the server normalises it and finds the customer.</param>
/// <param name="Compensated">The customer has been compensated for it. False when left out.</param>
public record UpsertMistakeRequest(
    [Required] DateOnly OccurredOn,
    [Required] Guid BranchId,
    [Required] string Responsible,
    Guid? AgentId,
    [Range(0, 1_000_000)] decimal? Value,
    [MaxLength(32)] string? CustomerNumber,
    [Required, MaxLength(4000)] string Notes,
    bool Compensated = false);
