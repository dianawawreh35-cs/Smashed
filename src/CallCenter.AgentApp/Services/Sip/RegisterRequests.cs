using SIPSorcery.SIP;

namespace CallCenter.AgentApp.Services.Sip;

/// <summary>
/// The two REGISTER requests the app sends itself rather than through
/// SIPSorcery's registration agent (N-05, 27 Sep evening): "forget every
/// address this extension has", at sign-in, and "forget this address", at
/// sign-out and exit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why at sign-in.</b> The PBX sends an extension's calls to the address it
/// last registered from. On 27 Sep that was a second copy of the app, hidden
/// or on another laptop, and the agent's own copy never rang. RFC 3261
/// §10.2.2: <c>Contact: *</c> with <c>Expires: 0</c> removes every binding the
/// registrar holds for the address of record. Issabel's chan_sip keeps one
/// contact per peer and takes <c>*</c> (or any <c>Expires: 0</c>) as "this
/// peer is unregistered", so the old address is gone before this copy
/// registers its own.
/// </para>
/// <para>
/// SIPSorcery's <c>SIPRegistrationUserAgent</c> cannot send either request:
/// its Contact is always its own, and its zero-expiry REGISTER at
/// <c>Stop()</c> is sent from the thread pool and never awaited, so an app
/// closing could end before it went (M-A01).
/// </para>
/// </remarks>
public static class RegisterRequests
{
    /// <summary><c>Contact: *</c>, <c>Expires: 0</c>: every binding for the extension.</summary>
    public static SIPRequest RemoveAll(string extension, string server) =>
        Build(extension, server, contact: null);

    /// <summary><c>Expires: 0</c> for one binding: this app's own.</summary>
    public static SIPRequest Remove(string extension, string server, SIPContactHeader contact) =>
        Build(extension, server, contact);

    private static SIPRequest Build(string extension, string server, SIPContactHeader? contact)
    {
        var registrar = SIPURI.ParseSIPURIRelaxed(server);
        var addressOfRecord = SIPURI.ParseSIPURIRelaxed($"{extension}@{registrar.Host}");

        var request = SIPRequest.GetRequest(
            SIPMethodsEnum.REGISTER,
            registrar,
            new SIPToHeader(null, addressOfRecord, null),
            new SIPFromHeader(null, addressOfRecord, CallProperties.CreateNewTag()));

        request.Header.Expires = 0;

        if (contact is null)
        {
            // SIPSorcery has no star contact, and it writes unknown headers as
            // given, so the header goes in as text.
            request.Header.Contact = [];
            request.Header.UnknownHeaders.Add(StarContact);
        }
        else
        {
            request.Header.Contact = [contact.CopyOf()];
        }

        return request;
    }

    /// <summary>
    /// <paramref name="request"/> again with the answer to the PBX's challenge,
    /// as a new transaction: the next CSeq and a new branch (RFC 3261 §8.1.3.5,
    /// §22.2).
    /// </summary>
    public static SIPRequest Authenticated(
        SIPRequest request, SIPResponse challenge, string username, string password)
    {
        var authenticated = request.DuplicateAndAuthenticate(
            challenge.Header.AuthenticationHeaders, username, password);

        authenticated.Header.CSeq = request.Header.CSeq + 1;
        authenticated.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();

        // The copy may have read "Contact: *" back as a contact of some kind,
        // or dropped it. Either way it is put back exactly as it was.
        if (IsRemoveAll(request))
        {
            authenticated.Header.Contact = [];
            authenticated.Header.UnknownHeaders.RemoveAll(IsStarContact);
            authenticated.Header.UnknownHeaders.Add(StarContact);
        }

        return authenticated;
    }

    /// <summary>Whether <paramref name="request"/> is the "every binding" kind.</summary>
    public static bool IsRemoveAll(SIPRequest request) =>
        request.Header.UnknownHeaders.Exists(IsStarContact);

    private const string StarContact = "Contact: *";

    private static bool IsStarContact(string header) =>
        header.Replace(" ", string.Empty).Equals("Contact:*", StringComparison.OrdinalIgnoreCase);
}
