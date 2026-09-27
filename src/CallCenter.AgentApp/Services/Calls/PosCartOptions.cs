namespace CallCenter.AgentApp.Services.Calls;

/// <summary>
/// The POS page opened for the caller when an incoming call is answered, from
/// the <c>PosCart</c> section of appsettings.json (A-85).
/// </summary>
/// <remarks>
/// Configuration rather than compiled in, so a change of address on the POS
/// side is an edit on each laptop, not a new build.
/// </remarks>
public class PosCartOptions
{
    public const string SectionName = "PosCart";

    /// <summary>Whether answering opens the page at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The page, with <c>{number}</c> where the caller's number goes. The number
    /// is put in as the POS knows it, the local form (<c>0599123456</c>), since
    /// it answers nothing else (A-67).
    /// </summary>
    public string UrlTemplate { get; set; } = "https://smashed-ps.com/app/cart/{number}";
}
