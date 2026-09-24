using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CallCenter.Shared.Contracts.Auth;
using CallCenter.Shared.Contracts.Classifications;
using CallCenter.Shared.Contracts.Communications;
using CallCenter.Shared.Contracts.Contacts;
using CallCenter.Shared.Contracts.Delivery;
using CallCenter.Shared.Contracts.Menu;
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

        /// <summary>
        /// The server looked and there is no such thing.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="ServerError"/> because for the caller
        /// lookup the two mean opposite things: 404 is "this number belongs to
        /// nobody, offer to add them" (A-11), and a server error is "we do not
        /// know". Folding them together told an agent that a regular customer
        /// was new whenever the server was unwell, which is how a second record
        /// for the same person gets typed.
        /// </remarks>
        NotFound,

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

    /// <summary>
    /// Uploads the audio of a finished call (A-31).
    /// </summary>
    /// <remarks>
    /// Multipart and streamed from disk: the audio is megabytes, and reading it
    /// into memory to post it would be a waste on a laptop that may be handling
    /// the next call already.
    ///
    /// A 404 here means the call has not reached the server yet, which the
    /// queue treats as "try again" rather than a failure.
    /// </remarks>
    public async Task<Result<object>> UploadRecordingAsync(
        string sipCallId, string extension, string localPath, CancellationToken ct = default)
    {
        if (!File.Exists(localPath))
        {
            // The file has gone - deleted by hand, or a previous upload
            // succeeded and the confirmation was lost. Either way there is
            // nothing to send and nothing to retry.
            logger.LogWarning("The recording for call {SipCallId} is no longer on disk", sipCallId);
            return Result<object>.Failed(ApiStatus.NotFound, "recording_missing");
        }

        return await SendAsync<object>(
            () =>
            {
                var content = new MultipartFormDataContent
                {
                    { new StringContent(sipCallId), "sipCallId" },
                    { new StringContent(extension), "extension" },
                };

                var audio = new StreamContent(
                    new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read));

                audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                content.Add(audio, "audio", Path.GetFileName(localPath));

                return new HttpRequestMessage(HttpMethod.Post, "api/communications/recordings")
                {
                    Content = content,
                };
            },
            authenticated: true,
            ct);
    }

    /// <summary>
    /// A customer's totals and last few calls, for the pop-up (A-10). Asked for
    /// after the contact itself, so the name is never waiting on this.
    /// </summary>
    public Task<Result<CallerCardDto>> GetCallerCardAsync(
        Guid contactId, int recent = 5, CancellationToken ct = default) =>
        SendAsync<CallerCardDto>(
            () => new HttpRequestMessage(
                HttpMethod.Get, $"api/contacts/{contactId}/card?recent={recent}"),
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

    /// <summary>
    /// Every blocked number, for the local cache the rejection reads (A-17).
    /// </summary>
    /// <remarks>
    /// Normalised numbers and nothing else: the app compares a caller against
    /// them, and no customer names need to reach an agent's laptop for that.
    /// </remarks>
    public Task<Result<BlockedNumbersDto>> GetBlockedNumbersAsync(CancellationToken ct = default) =>
        SendAsync<BlockedNumbersDto>(
            () => new HttpRequestMessage(HttpMethod.Get, "api/contacts/blocked-numbers"),
            authenticated: true,
            ct);

    /// <summary>
    /// Records one finished call (A-14). Safe to send again: the server keys a
    /// call on its Call-ID and extension, so a retry updates rather than
    /// duplicates.
    /// </summary>
    public Task<Result<CommunicationDto>> LogCallAsync(
        LogCallRequest request, CancellationToken ct = default) =>
        SendAsync<CommunicationDto>(
            () => new HttpRequestMessage(HttpMethod.Post, "api/communications/calls")
            {
                Content = JsonContent.Create(request),
            },
            authenticated: true,
            ct);

    /// <summary>
    /// The classification form, its types and the branches (A-40).
    /// </summary>
    /// <remarks>
    /// Fetched once at sign-in and kept. The supervisor's changes reach agents
    /// without reinstalling anything (S-40), so the version is checked again
    /// whenever the app has reason to.
    /// </remarks>
    public Task<Result<ClassificationFormDto>> GetClassificationFormAsync(
        CancellationToken ct = default) =>
        SendAsync<ClassificationFormDto>(
            () => new HttpRequestMessage(HttpMethod.Get, "api/classifications/form"),
            authenticated: true,
            ct);

    /// <summary>
    /// Records what a call was about, keyed on the call rather than its server
    /// id (A-40, A-04).
    /// </summary>
    /// <remarks>
    /// The app never learns a call's server id - the form opens while the call
    /// is still in progress, and the call itself is not reported until it ends.
    /// The SIP Call-ID and extension are the same pair the server keys the call
    /// on, so this always lands on the right one.
    /// </remarks>
    public Task<Result<ClassificationDto>> ClassifyByCallAsync(
        SaveClassificationByCallRequest request, CancellationToken ct = default) =>
        SendAsync<ClassificationDto>(
            () => new HttpRequestMessage(HttpMethod.Put, "api/classifications/by-call")
            {
                Content = JsonContent.Create(request),
            },
            authenticated: true,
            ct);

    /// <summary>
    /// What a call was already classified as, if anything (A-42).
    /// </summary>
    /// <remarks>
    /// Fetched before the form is drawn so it opens showing what is there
    /// rather than blank. A blank form over an existing classification is not
    /// merely unhelpful: saving it would replace a real answer with nothing,
    /// and the agent would have no way of knowing they had done it.
    /// </remarks>
    public Task<Result<ClassificationDto>> GetClassificationAsync(
        Guid communicationId, CancellationToken ct = default) =>
        SendAsync<ClassificationDto>(
            () => new HttpRequestMessage(
                HttpMethod.Get, $"api/classifications/{communicationId}"),
            authenticated: true,
            ct);

    /// <summary>
    /// Records what an older call was about, by its server id (A-42).
    /// </summary>
    /// <remarks>
    /// For classifying from the call log, where the id is known because the list
    /// came from the server. Sent directly rather than queued: the agent is
    /// looking at a list the server just gave them, so it is there.
    /// </remarks>
    public Task<Result<ClassificationDto>> ClassifyAsync(
        Guid communicationId, SaveClassificationRequest request, CancellationToken ct = default) =>
        SendAsync<ClassificationDto>(
            () => new HttpRequestMessage(
                HttpMethod.Put, $"api/classifications/{communicationId}")
            {
                Content = JsonContent.Create(request),
            },
            authenticated: true,
            ct);

    /// <summary>
    /// Writes the note on a missed, rejected or unanswered call — why it went that way
    /// (A-41). Those calls are never classified; this is what they take instead.
    /// </summary>
    public Task<Result<CommunicationDto>> SaveCallNotesAsync(
        Guid communicationId, string? notes, CancellationToken ct = default) =>
        SendAsync<CommunicationDto>(
            () => new HttpRequestMessage(
                HttpMethod.Put, $"api/communications/{communicationId}/notes")
            {
                Content = JsonContent.Create(new SaveCallNotesRequest(notes)),
            },
            authenticated: true,
            ct);

    /// <summary>
    /// The same note, keyed on the call rather than its server id (A-04). For
    /// the offline queue, behind the call it belongs to.
    /// </summary>
    public Task<Result<CommunicationDto>> SaveCallNotesByCallAsync(
        SaveCallNotesByCallRequest request, CancellationToken ct = default) =>
        SendAsync<CommunicationDto>(
            () => new HttpRequestMessage(HttpMethod.Put, "api/communications/by-call/notes")
            {
                Content = JsonContent.Create(request),
            },
            authenticated: true,
            ct);

    /// <summary>
    /// The signed-in agent's own calls (A-50). No agent id is sent and none is
    /// accepted: the token decides whose calls these are, so there is no request
    /// this app could make that would return another agent's (A-52).
    /// </summary>
    /// <param name="from">Inclusive lower bound on the call's start.</param>
    /// <param name="to">A date, not an instant: the whole of that day is included.</param>
    /// <param name="query">Number or customer name.</param>
    /// <param name="unclassifiedOnly">Only answered calls still needing a classification (A-41).</param>
    public Task<Result<IReadOnlyList<CommunicationDto>>> GetMyCallsAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? query = null,
        bool unclassifiedOnly = false,
        int limit = 100,
        CancellationToken ct = default)
    {
        var parameters = new List<string> { $"limit={limit}" };

        if (from is { } start)
        {
            parameters.Add($"from={Uri.EscapeDataString(start.ToString("o"))}");
        }

        if (to is { } end)
        {
            parameters.Add($"to={Uri.EscapeDataString(end.ToString("o"))}");
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            parameters.Add($"q={Uri.EscapeDataString(query.Trim())}");
        }

        if (unclassifiedOnly)
        {
            parameters.Add("unclassified=true");
        }

        return SendAsync<IReadOnlyList<CommunicationDto>>(
            () => new HttpRequestMessage(
                HttpMethod.Get, $"api/communications/mine?{string.Join("&", parameters)}"),
            authenticated: true,
            ct);
    }

    /// <summary>
    /// Which branch delivers to a place and what it costs (A-65). Read-only:
    /// only a supervisor changes these (S-58).
    /// </summary>
    public Task<Result<IReadOnlyList<DeliveryAreaDto>>> SearchDeliveryAreasAsync(
        string? query, CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<DeliveryAreaDto>>(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                $"api/delivery-areas?q={Uri.EscapeDataString(query ?? string.Empty)}"),
            authenticated: true,
            ct);

    /// <summary>
    /// The menu, or the part of it matching what was typed (A-66). Read-only:
    /// only a supervisor changes it (S-59).
    /// </summary>
    public Task<Result<IReadOnlyList<MenuItemDto>>> SearchMenuAsync(
        string? query, CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<MenuItemDto>>(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                $"api/menu?q={Uri.EscapeDataString(query ?? string.Empty)}"),
            authenticated: true,
            ct);

    /// <summary>
    /// One menu item's picture (A-66).
    /// </summary>
    /// <remarks>
    /// Raw bytes rather than JSON, and fetched per row as the list renders: the
    /// list is what the agent reads, and it should not wait on a megabyte of
    /// photographs. The server marks these cacheable for a day.
    /// </remarks>
    public async Task<Result<byte[]>> GetMenuImageAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/menu/{id}/image");

            if (session.AccessToken is { } token)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                return Result<byte[]>.Failed(ApiStatus.ServerError, "image_unavailable");
            }

            return Result<byte[]>.Ok(await response.Content.ReadAsByteArrayAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A missing picture is not worth a message to the agent; the row
            // simply shows without one.
            return Result<byte[]>.Failed(ApiStatus.Unreachable, "server_unreachable");
        }
    }

    /// <summary>
    /// The audio of one of the agent's own calls, to play in the call log (A-51).
    /// </summary>
    /// <remarks>
    /// Fetched whole, into memory, rather than handed to a player as a URL: no
    /// Windows player sends the bearer token, and writing the audio to a temp
    /// file would leave a customer's call on the laptop after the agent closed
    /// it. A call of a few minutes is a few megabytes.
    ///
    /// <b>Only the headers are held to the usual timeout.</b> The body of an
    /// hour-long call can take longer than ten seconds over the VPN, and cutting
    /// it off there would read as "this recording is broken". The caller's
    /// token ends it instead — closing the call cancels the download.
    ///
    /// The server decides whose recording this is (A-52): another agent's call
    /// answers <c>not_your_call</c>, and nothing here checks it again.
    /// </remarks>
    /// <returns>
    /// The WAV bytes, or the server's reason: <c>recording_not_found</c> (never
    /// recorded), <c>recording_expired</c> (deleted by retention, A-33) or
    /// <c>not_your_call</c>.
    /// </returns>
    public async Task<Result<byte[]>> GetRecordingAsync(
        Guid communicationId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/recordings/{communicationId}");

        if (session.AccessToken is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode switch
                {
                    HttpStatusCode.NotFound => ApiStatus.NotFound,
                    HttpStatusCode.Unauthorized => ApiStatus.Unauthorized,
                    _ => ApiStatus.ServerError,
                };

                return Result<byte[]>.Failed(
                    status, await ReadErrorCodeAsync(response, LoginErrorCodes.ServerError, ct));
            }

            return Result<byte[]>.Ok(await response.Content.ReadAsByteArrayAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                   && !ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "The recording of call {CommunicationId} could not be fetched ({Reason})",
                communicationId, ex.Message);

            return Result<byte[]>.Failed(ApiStatus.Unreachable, LoginErrorCodes.ServerUnreachable);
        }
    }

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

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Not logged as a failure: for the caller lookup and the
                // classification of an unclassified call, "there is none" is a
                // normal answer rather than something that went wrong.
                return Result<T>.Failed(
                    ApiStatus.NotFound,
                    await ReadErrorCodeAsync(response, LoginErrorCodes.ServerError, ct));
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
