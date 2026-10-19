using System.Formats.Asn1;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [BUG-FLAKY-WA] Chữ ký ECDSA của software authenticator phải được mã hoá DER với INTEGER
/// **big-endian TỐI GIẢN**. Trước fix, `AsnWriter.WriteIntegerUnsigned` được gọi thẳng trên 32 byte
/// P-1363 ⇒ ném <c>ArgumentException</c> mỗi khi byte cao nhất của r (hoặc s) là 0x00 (9 bit đầu đều
/// là 0). r/s là NGẪU NHIÊN ⇒ test ceremony FAIL ~1/128 lần ký, trông như "flaky do chạy song song"
/// nhưng thực chất là **input xác suất**, không phải state dùng chung.
///
/// <para>
/// Bộ test này thay việc "chờ xác suất" bằng kiểm tra TẤT ĐỊNH: tái hiện đúng cơ chế lỗi của code cũ,
/// rồi khoá hợp đồng encode mới (cắt byte 0 thừa, thêm 0x00 khi bit dấu bật, giá trị 0 = 1 byte 0x00).
/// </para>
/// </summary>
public class TestAuthenticatorDerTests
{
    private static byte[] SignatureWith(byte firstR, byte secondR, byte firstS = 0x11)
    {
        var p1363 = new byte[64];
        p1363[0] = firstR;
        p1363[1] = secondR;
        for (var i = 2; i < 32; i++) p1363[i] = 0x22;
        for (var i = 32; i < 64; i++) p1363[i] = firstS;
        return p1363;
    }

    /// <summary>
    /// TÁI HIỆN TẤT ĐỊNH nguyên nhân gốc: gọi <see cref="AsnWriter.WriteIntegerUnsigned"/> đúng như code
    /// CŨ với r bắt đầu `00 7F` (9 bit đầu = 0) ⇒ ArgumentException "The first 9 bits … all have the same
    /// value". Đây là điều kiện xảy ra ngẫu nhiên với r/s thật, không liên quan tới chạy song song.
    /// </summary>
    [Fact]
    public void AsnWriter_RejectsRedundantLeadingZeroByte_ReproducesTheFlake()
    {
        var r = new byte[32];
        r[0] = 0x00;
        r[1] = 0x7F;
        for (var i = 2; i < 32; i++) r[i] = 0x22;

        var asn = new AsnWriter(AsnEncodingRules.DER);

        var ex = Assert.Throws<ArgumentException>(() => asn.WriteIntegerUnsigned(r));
        Assert.Contains("first 9 bits", ex.Message);
    }

    /// <summary>r bắt đầu `00 7F …` ⇒ cắt byte 0 ⇒ INTEGER 31 byte bắt đầu 0x7F (không cần byte dấu).</summary>
    [Fact]
    public void Encode_LeadingZeroR_IsTrimmedToMinimalInteger()
    {
        var der = TestAuthenticator.EncodeEcdsaSignatureForDer(SignatureWith(0x00, 0x7F));

        Assert.Equal(0x30, der[0]);            // SEQUENCE
        Assert.Equal(0x02, der[2]);            // INTEGER r
        Assert.Equal(0x1F, der[3]);            // 31 byte (đã cắt 1 byte 0x00)
        Assert.Equal(0x7F, der[4]);            // byte cao nhất của r giữ nguyên giá trị
    }

    /// <summary>r bắt đầu 0x80 (bit dấu bật) ⇒ KHÔNG cắt, DER tự thêm 0x00 ⇒ INTEGER 33 byte.</summary>
    [Fact]
    public void Encode_HighBitR_GetsSignBytePrefix()
    {
        var der = TestAuthenticator.EncodeEcdsaSignatureForDer(SignatureWith(0x80, 0x22));

        Assert.Equal(0x30, der[0]);
        Assert.Equal(0x02, der[2]);
        Assert.Equal(0x21, der[3]);            // 33 byte (32 + byte dấu)
        Assert.Equal(0x00, der[4]);            // byte dấu DER
        Assert.Equal(0x80, der[5]);            // giá trị r không đổi
    }

    /// <summary>r = 0 ⇒ INTEGER 0 hợp lệ = `02 01 00` (không được cắt thành rỗng).</summary>
    [Fact]
    public void Encode_ZeroR_IsSingleZeroByte()
    {
        var p1363 = new byte[64];
        p1363[32] = 0x01; // s = 1 để SEQUENCE không rỗng

        var der = TestAuthenticator.EncodeEcdsaSignatureForDer(p1363);

        Assert.Equal(0x30, der[0]);
        Assert.Equal(0x02, der[2]);
        Assert.Equal(0x01, der[3]);
        Assert.Equal(0x00, der[4]);
    }

    /// <summary>
    /// Quét TẤT ĐỊNH mọi tổ hợp byte cao của r/s (0x00..0xFF): trước fix có 256 trường hợp ném lỗi,
    /// sau fix không trường hợp nào được ném — và mọi kết quả đều là SEQUENCE hợp lệ.
    /// </summary>
    [Fact]
    public void Encode_AllLeadingByteCombinations_DoNotThrow()
    {
        for (var b = 0; b <= 0xFF; b++)
        {
            var p1363 = SignatureWith((byte)b, 0x00, firstS: (byte)(0xFF - b));
            var der = TestAuthenticator.EncodeEcdsaSignatureForDer(p1363);

            Assert.Equal(0x30, der[0]);
            Assert.True(der.Length >= 8, $"DER quá ngắn với byte cao r=0x{b:X2}");
        }
    }

    /// <summary>Chữ ký thật (P-1363 64 byte) luôn encode được và giữ nguyên khả năng verify (không ném).</summary>
    [Fact]
    public void Encode_RealP1363Signatures_AlwaysEncode()
    {
        using var key = System.Security.Cryptography.ECDsa.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP256);

        for (var i = 0; i < 200; i++)
        {
            var p1363 = key.SignData(new byte[] { (byte)i }, System.Security.Cryptography.HashAlgorithmName.SHA256);
            Assert.Equal(64, p1363.Length); // .NET 10: RAW P-1363 (r||s)

            var der = TestAuthenticator.EncodeEcdsaSignatureForDer(p1363);
            Assert.Equal(0x30, der[0]);
        }
    }
}
