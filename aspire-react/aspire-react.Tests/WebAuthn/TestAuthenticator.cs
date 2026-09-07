using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace aspire_react.Tests;

/// <summary>
/// [AUTH Phase 3] Minimal software authenticator for ceremony unit tests — builds REAL
/// WebAuthn wire formats (CBOR attestation object with fmt=none, COSE EC2/P-256 public key,
/// DER ECDSA assertions) the same way fido2-net-lib's own tests simulate CTAP2 devices.
/// </summary>
public static class TestAuthenticator
{
    public sealed class FakeCredential
    {
        public required byte[] CredentialId { get; init; }
        public required ECDsa PrivateKey { get; init; }
        public required byte[] PublicKeyX { get; init; }
        public required byte[] PublicKeyY { get; init; }
        public uint SignCount;
    }

    private const string Origin = "https://localhost:5173";
    private const string RpId = "localhost";

    public static FakeCredential CreateCredential()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var ecParams = key.ExportParameters(false);
        return new FakeCredential
        {
            CredentialId = RandomNumberGenerator.GetBytes(32),
            PrivateKey = key,
            PublicKeyX = ecParams.Q.X!,
            PublicKeyY = ecParams.Q.Y!
        };
    }

    /// <summary>Builds the navigator.credentials.create() response JSON for the given options.</summary>
    public static string AttestationResponse(CredentialFixture options, FakeCredential cred)
    {
        var clientDataJson = $$"""{"type":"webauthn.create","challenge":"{{options.Challenge}}","origin":"{{Origin}}","crossOrigin":false}""";
        var clientDataHash = SHA256.HashData(Encoding.UTF8.GetBytes(clientDataJson));

        // authData: rpIdHash(32) | flags(1) | signCount(4) | attestedCredentialData
        var authData = new List<byte>(120);
        authData.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(RpId)));
        authData.Add(0x45); // UP | UV | AT
        authData.AddRange(To4Bytes(0)); // initial counter
        authData.AddRange(new byte[16]); // AAGUID (all-zero)
        authData.AddRange(To2Bytes(cred.CredentialId.Length));
        authData.AddRange(cred.CredentialId);
        authData.AddRange(CoseEc2Key(cred)); // attested credential public key

        // attestationObject = CBOR map { "fmt": "none", "attStmt": {}, "authData": bytes }
        var attestationObject = CborMapRaw([
            (CborText("fmt"), CborText("none")),
            (CborText("attStmt"), CborMap()),
            (CborText("authData"), CborBytes(authData.ToArray()))
        ]);

        // NOTE: plain concatenation — JSON braces fight raw-string delimiters (CS9007).
        var idB64 = ToB64Url(cred.CredentialId);
        return "{\"id\":\"" + idB64 + "\",\"rawId\":\"" + idB64 + "\",\"type\":\"public-key\",\"response\":{"
            + "\"attestationObject\":\"" + ToB64Url(attestationObject) + "\","
            + "\"clientDataJSON\":\"" + ToB64Url(Encoding.UTF8.GetBytes(clientDataJson)) + "\","
            + "\"transports\":[\"internal\"],"
            + "\"publicKeyAlgorithm\":-7,"
            + "\"publicKey\":\"" + ToB64Url(CoseEc2Key(cred)) + "\","
            + "\"authenticatorData\":\"" + ToB64Url(authData.ToArray()) + "\"}}";
    }

    /// <summary>Builds the navigator.credentials.get() response JSON for the given options.</summary>
    public static string AssertionResponse(CredentialFixture options, FakeCredential cred, byte[] userHandle, uint? forcedCount = null)
    {
        cred.SignCount++;
        var counter = forcedCount ?? cred.SignCount;
        var clientDataJson = $$"""{"type":"webauthn.get","challenge":"{{options.Challenge}}","origin":"{{Origin}}","crossOrigin":false}""";
        var clientDataHash = SHA256.HashData(Encoding.UTF8.GetBytes(clientDataJson));

        var authData = new List<byte>(40);
        authData.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(RpId)));
        authData.Add(0x05); // UP | UV
        authData.AddRange(To4Bytes(counter));

        var signed = authData.ToArray().Concat(clientDataHash).ToArray();
        // NOTE: .NET 10 ECDsa.SignData returns RAW P-1363 (r||s, 64 bytes) — WebAuthn/fido2
        // expects ASN.1 DER → wrap with AsnWriter (verified empirically in Phase 3).
        var p1363 = cred.PrivateKey.SignData(signed, HashAlgorithmName.SHA256);
        var asn = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
        using (asn.PushSequence())
        {
            asn.WriteIntegerUnsigned(p1363[..32]);
            asn.WriteIntegerUnsigned(p1363[32..]);
        }
        var signature = asn.Encode();

        var idB64 = ToB64Url(cred.CredentialId);
        return "{\"id\":\"" + idB64 + "\",\"rawId\":\"" + idB64 + "\",\"type\":\"public-key\",\"response\":{"
            + "\"authenticatorData\":\"" + ToB64Url(authData.ToArray()) + "\","
            + "\"clientDataJSON\":\"" + ToB64Url(Encoding.UTF8.GetBytes(clientDataJson)) + "\","
            + "\"signature\":\"" + ToB64Url(signature) + "\","
            + "\"userHandle\":\"" + ToB64Url(userHandle) + "\"}}";
    }

    /// <summary>Parses the challenge (base64url string) out of options JSON produced by the service.</summary>
    public static CredentialFixture ParseOptions(string optionsJson)
    {
        using var doc = JsonDocument.Parse(optionsJson);
        var challenge = doc.RootElement.GetProperty("challenge").GetString()!;
        return new CredentialFixture(challenge, optionsJson);
    }

    public sealed record CredentialFixture(string Challenge, string RawJson);

    // ---------- CBOR helpers ----------

    private static byte[] To4Bytes(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
    private static byte[] To2Bytes(int v) => [(byte)(v >> 8), (byte)v];

    public static string ToB64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] CoseEc2Key(FakeCredential cred)
    {
        // COSE key: {1: kty=EC2(2), 3: alg=ES256(-7), -1: crv=P-256(1), -2: x, -3: y}
        return CborMapRaw([
            (CborInt(1), CborInt(2)),
            (CborInt(3), CborInt(-7)),
            (CborInt(-1), CborInt(1)),
            (CborInt(-2), CborBytes(cred.PublicKeyX)),
            (CborInt(-3), CborBytes(cred.PublicKeyY))
        ]);
    }

    private static byte[] CborInt(long value)
    {
        if (value >= 0)
        {
            if (value < 24) return [(byte)value];
            if (value <= 0xFF) return [0x18, (byte)value];
            if (value <= 0xFFFF) return [0x19, (byte)(value >> 8), (byte)value];
            return [0x1A, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
        }
        var n = -1 - value; // major type 1 encodes -1-n
        if (n < 24) return [(byte)(0x20 | n)];
        if (n <= 0xFF) return [0x38, (byte)n];
        if (n <= 0xFFFF) return [0x39, (byte)(n >> 8), (byte)n];
        return [0x3A, (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n];
    }

    private static byte[] CborText(string s)
    {
        var utf8 = Encoding.UTF8.GetBytes(s);
        return CborHead(3, utf8.Length).Concat(utf8).ToArray();
    }

    private static byte[] CborBytes(byte[] b)
        => CborHead(2, b.Length).Concat(b).ToArray();

    private static byte[] CborMap() => [0xA0];

    private static byte[] CborMapRaw(List<(byte[] Key, byte[] Value)> pairs)
    {
        var body = new List<byte>(CborHead(5, pairs.Count));
        foreach (var (k, v) in pairs)
        {
            body.AddRange(k);
            body.AddRange(v);
        }
        return body.ToArray();
    }

    private static byte[] CborHead(byte major, long length)
    {
        var m = (byte)(major << 5);
        if (length < 24) return [(byte)(m | length)];
        if (length <= 0xFF) return [(byte)(m | 24), (byte)length];
        if (length <= 0xFFFF) return [(byte)(m | 25), (byte)(length >> 8), (byte)length];
        return [(byte)(m | 26), (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length];
    }
}
