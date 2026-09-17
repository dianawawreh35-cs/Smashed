using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CallCenter.Shared.Contracts.Auth;
using CallCenter.Shared.Contracts.Contacts;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services;

/// <summary>
/// Calls to the server. Every method reports failure as a value rather than an
/// exception, because the app has to keep working when the server is down (A-04).
/// </summary>
public class ApiClient(HttpClient http, AgentSession session, ILogger<ApiClient> logger)
{
    /// <summary>The name this client is registered under in DI.</summary>
    public const string HttpClientName = "server";

    public enum ApiStatus
    {
        Ok,

        /// <summary>The credentials were refused, or the token has expired.</summary>
        Unauthorized,

        /// <summary>The server could not be reached, or did not answer in time.</summary>
        Unreachable,

        /// <summary>The server answered with an error.</summary>
        ServerError,
    }

    /// <summary>The outcome of a call: a value, or why there isn't one.</summary>
    /// <param name="Value">Null unless <paramref name="Status"/> is <see cref="ApiStatus.Ok"/>.</param>
    /// <param name="ErrorCode">
    /// One of <see cref="LoginErrorCodes"/>. The caller turns it into a message
    /// in the agent's language (A-80) — the server's own English text is for
    /// logs, not for the screen.
    /// </param>
    public record Result<T>(ApiStatus Status, T? Value, string? ErrorCode)
    {
        public bool IsOk => Status == ApiStatus.Ok;

        public static Result<T> Ok(T value) => new(ApiStatus.Ok, value, null);

        public static Result<T> Failed(ApiStatus status, string errorCode) =>
            new(status, default, errorCode);
    }

    /// <summary>Signs in (A-01).</summary>
    public Task<Result<LoginResponse>> LoginAsync(
        string login, string password, CancellationToken ct = default)
    {
        var request = new LoginRequest(login, password, LaptopInfo.LaptopId, LaptopInfo.AppVersion);

        return SendAsync<LoginResponse>(
            () => new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
            {
                Content = JsonContent.Create(request),
            },
            authenticated: false,
            ct);
    }

    /// <summary>Closes the server-side session (A-05). Best effort.</summary>
    public Task LogoutAsync(Guid sessionId, string reason, CancellationToken ct = default) =>
        SendAsync<object>(
            () => new HttpRequestMessage(HttpMethod.Post, "api/auth/logout")
            {
                Content = JsonContent.Create(new LogoutRequest(sessionId, reason)),
            },
            authenticated: true,
            ct);

    /// <summary>Searches the shared contact list by name, address or number (A-61).</summary>
    public Task<Result<IReadOnlyList<ContactSummaryDto>>> SearchContactsAsync(
        string? query, CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<ContactSummaryDto>>(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                $"api/contacts?q={Uri.EscapeDataString(query ?? string.Empty)}"),
            authenticated: true,
            ct);

    /// <summary>One contact in full (A-62).</summary>
    public Task<Result<ContactDto>> GetContactAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<ContactDto>(
            () => new HttpRequestMessage(HttpMethod.Get, $"api/contacts/{id}"),
            authenticated: true,
            ct);

    /// <summary>
    /// The contact a number belongs to (A-10, A-13). The lookup the
    /// incoming-call pop-up will use; a NotFound result means "New customer".
    /// </summary>
    public Task<Result<ContactDto>> FindContactByPhoneAsync(
        string number, CancellationToken ct = default) =>
        SendAsync<ContactDto>(
            () => new HttpRequestMessage(
                HttpMethod.Get, $"api/contacts/by-phone?number={Uri.EscapeDataString(number)}"),
            authenticated: true,
            ct);

    /// <summary>Contacts that already carry this name — the warning in A-63.</summary>
    public Task<Result<IReadOnlyList<ContactSummaryDto>>> FindContactsByNameAsync(
        string name, Guid? excluding = null, CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<ContactSummaryDto>>(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                $"api/contacts/by-name?name={Uri.EscapeDataString(name)}"
                + (excluding is null ? string.Empty : $"&excluding={excluding}")),
            authenticated: true,
            ct);

    /// <summary>Creates a contact (A-63).</summary>
    public Task<Result<ContactDto>> CreateContactAsync(
        UpsertContactRequest request, CancellationToken ct = default) =>
        SendAsync<ContactDto>(
            () => new HttpRequestMessage(HttpMethod.Post, "api/contacts")
            {
                Content = JsonContent.Create(request),
            },
            authenticated: true,
            ct);

    /// <summary>Replaces a contact's details and numbers (A-63).</summary>
    public Task<Result<ContactDto>> UpdateContactAsync(
        Guid id, UpsertContactRequest request, CancellationToken ct = default) =>
        SendAsync<ContactDto>(
            () => new HttpRequestMessage(HttpMethod.Put, $"api/contacts/{id}")
            {
                Content = JsonContent.Create(request),
            },
            authenticated: true,
            ct);

    /// <summary>Adds one number to a contact that already exists (A-63).</summary>
    public Task<Result<ContactDto>> AddContactPhoneAsync(
        Guid id, string number, CancellationToken ct = default) =>
        SendAsync<ContactDto>(
            () => new HttpRequestMessage(HttpMethod.Post, $"api/contacts/{id}/phones")
            {
                Content = JsonContent.Create(new AddPhoneRequest(number)),
            },
            authenticated: true,
            ct);

    /// <summary>Checks that the current token is still accepted.</summary>
    public Task<Result<CurrentUserDto>> GetCurrentUserAsync(CancellationToken ct = default) =>
        SendAsync<CurrentUserDto>(
            () => new HttpRequestMessage(HttpMethod.Get, "api/auth/me"),
            authenticated: true,
            ct);

    private async Task<Result<T>> SendAsync<T>(
        Func<HttpRequestMessage> build, bool authenticated, CancellationToken ct)
    {
        using var request = build();

        if (authenticated && session.AccessToken is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        try
        {
            using var response = await http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return Result<T>.Failed(
                    ApiStatus.Unauthorized,
                    await ReadErrorCodeAsync(response, LoginErrorCodes.InvalidCredentials, ct));
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "{Method} {Path} failed: {StatusCode}",
                    request.Method, request.RequestUri, (int)response.StatusCode);

                return Result<T>.Failed(
                    ApiStatus.ServerError,
                    await ReadErrorCodeAsync(response, LoginErrorCodes.ServerError, ct));
            }

            // Endpoints such as logout answer 204 with no body.
            if (response.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(object))
            {
                return Result<T>.Ok(default!);
            }

            var value = await response.Content.ReadFromJsonAsync<T>(ct);

            return value is null
                ? Result<T>.Failed(ApiStatus.ServerError, LoginErrorCodes.ServerError)
                : Result<T>.Ok(value);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Expected whenever the network or the mini PC is down. A warning,
            // not an error: A-04 says the app carries on regardless.
            logger.LogWarning(
                "{Method} {Path}: the server could not be reached ({Reason})",
                request.Method, request.RequestUri, ex.Message);

            return Result<T>.Failed(ApiStatus.Unreachable, LoginErrorCodes.ServerUnreachable);
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex, "{Method} {Path} returned something that is not valid JSON",
                request.Method, request.RequestUri);

            return Result<T>.Failed(ApiStatus.ServerError, LoginErrorCodes.ServerError);
        }
    }

    /// <summary>
    /// Reads the <c>code</c> member out of an RFC 7807 problem response, so the
    /// caller can show the agent a translated message.
    /// </summary>
    private static async Task<string> ReadErrorCodeAsync(
        HttpResponseMessage response, string fallback, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemShape>(ct);
            if (!string.IsNullOrWhiteSpace(problem?.Code))
            {
                return problem.Code;
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or HttpRequestException)
        {
            // An older server, or an error page from something in between.
        }

        return fallback;
    }

    private record ProblemShape(string? Title, string? Detail, string? Code);
}
