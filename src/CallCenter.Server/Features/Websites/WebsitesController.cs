using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Websites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Websites;

/// <summary>
/// The websites inside the Agent App (A-88): the supervisor's list, and the
/// tabs each app opens at sign-in.
/// </summary>
[ApiController]
[Route("api/websites")]
public class WebsitesController(WebsitesService websites) : ControllerBase
{
    /// <summary>Every tab, hidden ones too. No passwords: only whether one is stored.</summary>
    [HttpGet]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<IReadOnlyList<WebsiteDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WebsiteDto>>> List(CancellationToken ct) =>
        Ok(await websites.ListAsync(ct));

    /// <summary>
    /// The tabs on show, for the Agent App, with the shared logins' passwords.
    /// Any signed-in account: the supervisor's laptop runs the Agent App too.
    /// </summary>
    [HttpGet("mine")]
    [Authorize(AuthPolicies.SignedIn)]
    [ProducesResponseType<IReadOnlyList<AgentWebsiteDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AgentWebsiteDto>>> Mine(CancellationToken ct)
    {
        // Passwords in the body: nothing between here and the app may keep it.
        Response.Headers.CacheControl = "no-store";
        return Ok(await websites.ForAgentAsync(ct));
    }

    [HttpPost]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<WebsiteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WebsiteDto>> Create(UpsertWebsiteRequest request, CancellationToken ct)
    {
        var (website, failure) = await websites.CreateAsync(request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(website);
    }

    /// <summary>Changes a tab. A null password keeps the stored one; an empty one removes it.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<WebsiteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WebsiteDto>> Update(Guid id, UpsertWebsiteRequest request, CancellationToken ct)
    {
        var (website, failure) = await websites.UpdateAsync(id, request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(website);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var failure = await websites.DeleteAsync(id, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : NoContent();
    }

    private ObjectResult Problem(WebsitesService.Failure failure)
    {
        var (status, code, detail) = failure switch
        {
            WebsitesService.Failure.NotFound =>
                (StatusCodes.Status404NotFound, "website_not_found", "No such website."),
            WebsitesService.Failure.BadName =>
                (StatusCodes.Status400BadRequest, "bad_name", "Both the Arabic and the English name are needed."),
            WebsitesService.Failure.BadUrl =>
                (StatusCodes.Status400BadRequest, "bad_url", "The address must be a whole web address, starting https://."),
            WebsitesService.Failure.BadCartUrl =>
                (StatusCodes.Status400BadRequest, "bad_cart_url",
                    "The cart address must be a whole web address with {number} where the caller's number goes."),
            WebsitesService.Failure.CartTaken =>
                (StatusCodes.Status409Conflict, "cart_taken", "Another tab already opens the caller's cart."),
            WebsitesService.Failure.BadLogin =>
                (StatusCodes.Status400BadRequest, "bad_login",
                    "A login the app types in needs a username."),
            _ => (StatusCodes.Status400BadRequest, "invalid_request", "The website could not be saved."),
        };

        var problem = new ProblemDetails { Title = "Website not saved", Detail = detail, Status = status };
        problem.Extensions["code"] = code;

        return StatusCode(status, problem);
    }
}
