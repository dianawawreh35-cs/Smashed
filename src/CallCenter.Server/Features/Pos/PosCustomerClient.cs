using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CallCenter.Shared.Phone;
using Microsoft.Extensions.Options;

namespace CallCenter.Server.Features.Pos;

/// <summary>One customer as the POS returns them (A-67).</summary>
public record PosCustomer(
    [property: JsonPropertyName("CU_ID")] int Id,
    [property: JsonPropertyName("Name")] string? Name,
    [property: JsonPropertyName("Phone")] string? Phone,
    [property: JsonPropertyName("Phone2")] string? Phone2,
    [property: JsonPropertyName("Address1")] string? Address,
    [property: JsonPropertyName("City")] string? City,
    [property: JsonPropertyName("Notes")] string? Notes,
    [property: JsonPropertyName("CU_BL")] bool Blacklisted);

/// <summary>Asks the POS who a number belongs to.</summary>
public interface IPosCustomerLookup
{
    /// <summary>
    /// The POS's customer for <paramref name="normalisedNumber"/>, or null when
    /// it has none. Throws when the POS could not be asked (down, refused the
    /// token), so a failed request is never taken for "not a customer".
    /// </summary>
    Task<PosCustomer?> FindAsync(string normalisedNumber, CancellationToken ct = default);
}

/// <summary>
/// <c>GET {BaseUrl}{number}</c> with the bearer token (A-67).
/// </summary>
/// <remarks>
/// Found on 26 Sep, by trying it: the POS answers only to the local form
/// (<c>0569498581</c>; dashes are fine), gives <b>404</b> for a number it does
/// not know rather than an empty list, and gives 404 for <c>970…</c> and
/// <c>+970…</c> as well. So the number is sent through
/// <see cref="PhoneNormalizer.ToNational"/>, and a 404 is the one answer that
/// means "not a customer". It answers in about half a second.
/// </remarks>
public class PosCustomerClient(HttpClient http, IOptions<PosLookupOptions> options) : IPosCustomerLookup
{
    public async Task<PosCustomer?> FindAsync(string normalisedNumber, CancellationToken ct = default)
    {
        var national = PhoneNormalizer.ToNational(normalisedNumber);
        if (national is null)
        {
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get, options.Value.BaseUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(national));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.Token);

        using var response = await http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var customers = await response.Content.ReadFromJsonAsync<List<PosCustomer>>(ct) ?? [];

        // One customer per number is what the POS has shown so far. Should it
        // ever return two, the one whose own number is this one is the answer.
        return customers.FirstOrDefault(c => PhoneNormalizer.Normalize(c.Phone) == normalisedNumber)
               ?? customers.FirstOrDefault();
    }
}
