namespace CallCenter.Shared.Contracts.Pbx;

/// <summary>
/// The PBX blacklist (S-46) as the settings screen shows it: the extension the
/// server dials <c>*30</c> and <c>*31</c> from, and how far the PBX has caught
/// up with the Blocked flags.
/// </summary>
/// <param name="Extension">The server's own extension on the PBX. Blank turns the blacklist off.</param>
/// <param name="SecretSet">
/// Whether its password is stored. The password itself never leaves the server:
/// the screen can replace it, not read it.
/// </param>
/// <param name="Configured">Extension, password and the PBX address (<c>pbx.host</c>) are all set, so the server dials.</param>
/// <param name="OnPbx">Numbers the server has put on the PBX's blacklist and not taken off.</param>
/// <param name="Waiting">Numbers still to be added or removed, the failed ones included.</param>
/// <param name="Failures">The numbers whose last call failed, newest first.</param>
/// <param name="LastSucceededAt">When a number was last added or removed.</param>
public record PbxBlacklistDto(
    string Extension,
    bool SecretSet,
    bool Configured,
    int OnPbx,
    int Waiting,
    IReadOnlyList<PbxBlacklistFailureDto> Failures,
    DateTimeOffset? LastSucceededAt);

/// <summary>A number the PBX has not caught up with, and why.</summary>
/// <param name="Number">As keyed into the PBX: the local form, <c>0599123456</c>.</param>
/// <param name="Adding">True for a <c>*30</c> that failed, false for a <c>*31</c>.</param>
/// <param name="Error">What went wrong, in words a supervisor can act on.</param>
public record PbxBlacklistFailureDto(
    string Number,
    bool Adding,
    string Error,
    int Attempts,
    DateTimeOffset At);

/// <summary>Changes the extension the server dials from (S-46).</summary>
/// <param name="Extension">Blank turns the blacklist off.</param>
/// <param name="Secret">A new password, or blank to keep the stored one.</param>
public record UpdatePbxBlacklistRequest(
    string? Extension,
    string? Secret);
