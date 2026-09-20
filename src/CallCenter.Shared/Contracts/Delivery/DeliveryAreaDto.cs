using System.ComponentModel.DataAnnotations;

namespace CallCenter.Shared.Contracts.Delivery;

/// <summary>
/// One place the restaurant delivers to (A-65, S-58).
/// </summary>
/// <param name="Price">
/// Zero is a real price, not a missing one — areas beside a branch deliver free.
/// </param>
public record DeliveryAreaDto(
    Guid Id,
    string Name,
    Guid BranchId,
    string BranchName,
    decimal Price,
    bool IsActive);

/// <summary>One of the restaurant's branches (S-41).</summary>
public record BranchDto(Guid Id, string Name, bool IsActive);

/// <summary>Creates or updates one area (S-58).</summary>
public record UpsertDeliveryAreaRequest(
    [Required, MaxLength(200)] string Name,
    [Required] Guid BranchId,
    [Range(0, 10000)] decimal Price,
    bool IsActive = true);

/// <summary>
/// Adds many areas at once, pasted from the branch's own list (S-58).
/// </summary>
/// <remarks>
/// The restaurant keeps these in a spreadsheet, one per branch, and there are
/// 230 of them. Typing each into a form is how a supervisor decides not to keep
/// the list up to date at all, so the fast path is the one that has to work:
/// pick a branch, paste two columns, save.
/// </remarks>
/// <param name="Lines">
/// One area per line, as <c>name</c> then <c>price</c> separated by a tab or a
/// comma. Tabs first, because that is what a paste out of Excel gives.
/// </param>
/// <param name="Replace">
/// True empties this branch's areas before adding, so a pasted list is the whole
/// list. False adds and updates, leaving anything not mentioned alone.
/// </param>
public record ImportDeliveryAreasRequest(
    [Required] Guid BranchId,
    [Required] string Lines,
    bool Replace = false);

/// <summary>
/// What an import did, line by line where it went wrong (S-58).
/// </summary>
/// <remarks>
/// A count alone would not do. Pasting 168 lines and being told "154 added"
/// leaves the supervisor to find the other fourteen themselves, so every
/// rejected line is returned with its number and the reason.
/// </remarks>
public record ImportDeliveryAreasResult(
    int Added,
    int Updated,
    int Removed,
    IReadOnlyList<ImportProblem> Problems);

/// <param name="Line">1-based, counting every line pasted including blank ones.</param>
/// <param name="Reason">
/// A code the client translates (A-80): <c>no_price</c>, <c>bad_price</c>,
/// <c>no_name</c>, <c>duplicate_in_paste</c>, <c>other_branch</c>.
/// </param>
public record ImportProblem(int Line, string Text, string Reason, string? Detail = null);
