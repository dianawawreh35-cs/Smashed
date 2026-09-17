using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Contacts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Contacts;

/// <summary>
/// The shared contact list (A-60 to A-63).
/// </summary>
/// <remarks>
/// Open to any signed-in account, not supervisors only: A-61 says every agent
/// sees every contact, and A-63 says agents create and edit them. The VIP and
/// Blocked flags are the exception and belong to the supervisor alone (S-45),
/// which is why no endpoint here touches them.
/// </remarks>
[ApiController]
[Route("api/contacts")]
[Authorize(AuthPolicies.SignedIn)]
public class ContactsController(ContactsService contacts) : ControllerBase
{
    /// <summary>
    /// Searches by name, address or number (A-61). A query of digits is treated
    /// as a number; anything else matches name and address.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ContactSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ContactSummaryDto>>> Search(
        [FromQuery] string? q, CancellationToken ct) =>
        Ok(await contacts.SearchAsync(q, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ContactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ContactDto>> Get(Guid id, CancellationToken ct)
    {
        var contact = await contacts.GetAsync(id, ct);
        return contact is null ? NotFound() : Ok(contact);
    }

    /// <summary>
    /// Finds the contact a number belongs to (A-10, A-13). This is what the
    /// incoming-call pop-up calls; 404 means "New customer".
    /// </summary>
    [HttpGet("by-phone")]
    [ProducesResponseType<ContactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ContactDto>> ByPhone(
        [FromQuery] string number, CancellationToken ct)
    {
        var contact = await contacts.FindByPhoneAsync(number, ct);
        return contact is null ? NotFound() : Ok(contact);
    }

    [HttpPost]
    [ProducesResponseType<ContactDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ContactDto>> Create(UpsertContactRequest request, CancellationToken ct)
    {
        var (contact, failure, duplicate) =
            await contacts.CreateAsync(request, User.GetRequiredUserId(), ct);

        return failure is not null
            ? Problem(failure.Value, duplicate)
            : CreatedAtAction(nameof(Get), new { id = contact!.Id }, contact);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ContactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ContactDto>> Update(
        Guid id, UpsertContactRequest request, CancellationToken ct)
    {
        var (contact, failure, duplicate) =
            await contacts.UpdateAsync(id, request, User.GetRequiredUserId(), ct);

        return failure is not null ? Problem(failure.Value, duplicate) : Ok(contact);
    }

    /// <summary>
    /// Turns a refusal into a reply. Each carries a <c>code</c> so the client
    /// shows its own translation (A-80); a duplicate also carries the contact
    /// that already holds the number, so the screen can offer to open it.
    /// </summary>
    private ObjectResult Problem(ContactsService.Failure failure, DuplicateNumberDto? duplicate)
    {
        var (status, code, detail) = failure switch
        {
            ContactsService.Failure.NotFound =>
                (StatusCodes.Status404NotFound, "contact_not_found", "No such contact."),
            ContactsService.Failure.NoUsableNumber =>
                (StatusCodes.Status400BadRequest, "no_usable_number",
                    "At least one real phone number is needed."),
            ContactsService.Failure.DuplicateNumber =>
                (StatusCodes.Status409Conflict, "duplicate_number",
                    "That number already belongs to another contact."),
            _ => (StatusCodes.Status400BadRequest, "invalid_request", "The contact could not be saved."),
        };

        var problem = new ProblemDetails
        {
            Title = "Contact not saved",
            Detail = detail,
            Status = status,
        };

        problem.Extensions["code"] = code;

        if (duplicate is not null)
        {
            problem.Extensions["duplicate"] = duplicate;
        }

        return StatusCode(status, problem);
    }
}
