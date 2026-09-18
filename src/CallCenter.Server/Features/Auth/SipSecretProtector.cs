using System.Security.Cryptography;
using System.Text;

namespace CallCenter.Server.Features.Auth;

/// <summary>
/// Encrypts the SIP secret held in <c>users.sip_secret</c> (N-05).
/// </summary>
/// <remarks>
/// A database dump therefore does not hand over the extensions' passwords; the
/// key lives in the server's environment, next to the JWT signing key. Losing
/// the key means re-entering the extensions in the supervisor app, not losing
/// any call data.
/// </remarks>
public interface ISipSecretProtector
{
    /// <summary>Encrypts a plain SIP secret for storage. Null in, null out.</summary>
    string? Protect(string? plaintext);

    /// <summary>
    /// Decrypts a stored secret. Returns null when the value cannot be read —
    /// a wrong or rotated key — so login degrades to "phone not configured"
    /// rather than throwing.
    /// </summary>
    string? Unprotect(string? ciphertext);
}

/// <summary>AES-GCM implementation. Ciphertext is <c>v1.nonce.tag.data</c>, base64url per part.</summary>
public class SipSecretProtector : ISipSecretProtector
{
    /// <summary>Configuration key holding the key material.</summary>
    public const string KeyConfigurationPath = "Security:SipSecretKey";

    private const string Prefix = "v1";
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly byte[] _key;
    private readonly ILogger<SipSecretProtector> _logger;

    public SipSecretProtector(IConfiguration configuration, ILogger<SipSecretProtector> logger)
    {
        _logger = logger;

        var configured = configuration[KeyConfigurationPath];
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"'{KeyConfigurationPath}' is not set. Generate one with "
                + "`openssl rand -base64 32` and set it in the server environment (runbook step 5).");
        }

        // A 32-byte key however it was written: base64 from openssl, or a
        // passphrase that gets hashed to the right length.
        _key = TryDecodeBase64(configured, out var raw) && raw.Length == 32
            ? raw
            : SHA256.HashData(Encoding.UTF8.GetBytes(configured));
    }

    public string? Protect(string? plaintext)
    {
        if (plaintext is null)
        {
            return null;
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, data, cipher, tag);

        return string.Join('.', Prefix, Base64Url(nonce), Base64Url(tag), Base64Url(cipher));
    }

    public string? Unprotect(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            return null;
        }

        var parts = ciphertext.Split('.');
        if (parts.Length != 4 || parts[0] != Prefix)
        {
            _logger.LogWarning("A stored SIP secret is not in the expected format; treating it as unset.");
            return null;
        }

        try
        {
            var nonce = FromBase64Url(parts[1]);
            var tag = FromBase64Url(parts[2]);
            var cipher = FromBase64Url(parts[3]);
            var plain = new byte[cipher.Length];

            using var aes = new AesGcm(_key, TagBytes);
            aes.Decrypt(nonce, cipher, tag, plain);

            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            // Never log the value itself.
            _logger.LogWarning(ex, "A stored SIP secret could not be decrypted; treating it as unset.");
            return null;
        }
    }

    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        bytes = [];
        Span<byte> buffer = stackalloc byte[64];
        if (!Convert.TryFromBase64String(value.Trim(), buffer, out var written))
        {
            return false;
        }

        bytes = buffer[..written].ToArray();
        return true;
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '='));
    }
}
