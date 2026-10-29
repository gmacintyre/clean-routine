using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CleanRoutine.Push;

/// <summary>
/// Web Push without third-party packages: VAPID signing (RFC 8292) and
/// aes128gcm payload encryption (RFC 8291 / RFC 8188), using only .NET's built-in crypto.
/// </summary>
public static class WebPushCrypto
{
    private const int RecordSize = 4096;

    public static (string PublicKey, string PrivateKey) GenerateVapidKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(includePrivateParameters: true);
        return (Base64Url.Encode(UncompressedPoint(p.Q)), Base64Url.Encode(p.D!));
    }

    /// <summary>The value for the Authorization header: "vapid t=&lt;jwt&gt;, k=&lt;public key&gt;".</summary>
    public static string VapidAuthorization(string endpoint, string subject, string publicKey, string privateKey,
        DateTimeOffset now)
    {
        var uri = new Uri(endpoint);
        var audience = $"{uri.Scheme}://{uri.Authority}";

        var header = Base64Url.Encode("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"u8.ToArray());
        var exp = now.AddHours(12).ToUnixTimeSeconds();
        var claims = $"{{\"aud\":{Quote(audience)},\"exp\":{exp},\"sub\":{Quote(subject)}}}";
        var unsigned = header + "." + Base64Url.Encode(Encoding.UTF8.GetBytes(claims));

        var pub = Base64Url.Decode(publicKey);
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Base64Url.Decode(privateKey),
            Q = PointFromUncompressed(pub),
        });
        // .NET signs in IEEE P1363 form (r || s), which is what JWT ES256 expects.
        var signature = ecdsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256);
        return $"vapid t={unsigned}.{Base64Url.Encode(signature)}, k={publicKey}";
    }

    /// <summary>Encrypt a payload for one browser subscription (Content-Encoding: aes128gcm).</summary>
    public static byte[] Encrypt(byte[] plaintext, string p256dh, string auth)
    {
        using var serverKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var salt = RandomNumberGenerator.GetBytes(16);
        return Encrypt(plaintext, Base64Url.Decode(p256dh), Base64Url.Decode(auth), serverKey, salt);
    }

    internal static byte[] Encrypt(byte[] plaintext, byte[] uaPublic, byte[] authSecret,
        ECDiffieHellman serverKey, byte[] salt)
    {
        var asPublic = UncompressedPoint(serverKey.ExportParameters(false).Q);

        using var uaKey = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = PointFromUncompressed(uaPublic),
        });
        var ecdhSecret = serverKey.DeriveRawSecretAgreement(uaKey.PublicKey);

        // RFC 8291 section 3.4
        var prkKey = HKDF.Extract(HashAlgorithmName.SHA256, ecdhSecret, authSecret);
        var keyInfo = Concat("WebPush: info\0"u8.ToArray(), uaPublic, asPublic);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, prkKey, 32, keyInfo);

        // RFC 8188 section 2.2
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, "Content-Encoding: nonce\0"u8.ToArray());

        // Single record: plaintext followed by the 0x02 "last record" delimiter.
        var padded = new byte[plaintext.Length + 1];
        plaintext.CopyTo(padded, 0);
        padded[^1] = 0x02;
        if (padded.Length + 16 > RecordSize) throw new ArgumentException("Payload too large for one record.");

        var cipher = new byte[padded.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(cek, 16))
        {
            aes.Encrypt(nonce, padded, cipher, tag);
        }

        var header = new byte[16 + 4 + 1 + asPublic.Length];
        salt.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), RecordSize);
        header[20] = (byte)asPublic.Length;
        asPublic.CopyTo(header, 21);

        return Concat(header, cipher, tag);
    }

    private static byte[] UncompressedPoint(ECPoint q) => Concat([0x04], q.X!, q.Y!);

    private static ECPoint PointFromUncompressed(byte[] point)
    {
        if (point.Length != 65 || point[0] != 0x04) throw new ArgumentException("Expected a 65-byte uncompressed P-256 point.");
        return new ECPoint { X = point[1..33], Y = point[33..65] };
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }

    private static string Quote(string s) => System.Text.Json.JsonSerializer.Serialize(s);
}

public static class Base64Url
{
    public static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string s)
    {
        s = s.Trim().Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
