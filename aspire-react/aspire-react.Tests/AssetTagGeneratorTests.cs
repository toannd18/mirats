using aspire_react.Server.Infrastructure.Services;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// Task ASSET-TAG-AUTO — tests for the Asset Tag format rendering + setting validation.
/// The per-(company, year) counter path uses raw SQL FOR UPDATE + a real transaction, which the
/// EF InMemory provider cannot execute — that path is verified against the real API (concurrency
/// test) per the Task O/O-FIX convention. These tests cover the DB-free pieces.
/// </summary>
public class AssetTagGeneratorTests
{
    [Theory]
    [InlineData("AST-{COMPANY}-{YYYY}-{SEQ:3}", "ABC", 2026, 1, "AST-ABC-2026-001")]
    [InlineData("AST-{COMPANY}-{YYYY}-{SEQ:3}", "XYZ", 2026, 12, "AST-XYZ-2026-012")]
    [InlineData("AST-{COMPANY}-{YYYY}-{SEQ:3}", "NOCO", 2026, 999, "AST-NOCO-2026-999")]
    [InlineData("AST-{COMPANY}-{YYYY}-{SEQ:3}", "ABC", 2026, 1000, "AST-ABC-2026-1000")] // exceeds padding → wider, still valid
    [InlineData("AST-{COMPANY}-{YYYY}-{SEQ:5}", "ABC", 2026, 7, "AST-ABC-2026-00007")]
    [InlineData("{SEQ:2}/{YYYY}/AST-{COMPANY}", "ABC", 2026, 3, "03/2026/AST-ABC")]        // tokens anywhere, literal preserved
    [InlineData("AST-{YYYY}-{SEQ:1}", "ABC", 2026, 9, "AST-2026-9")]                        // no {COMPANY} → omitted
    public void Render_FormatsYearSeqCompany(string format, string company, int year, long seq, string expected)
    {
        Assert.Equal(expected, AssetTagGenerator.Render(format, year, seq, company));
    }

    [Fact]
    public void Render_DefaultFormat_ProducesCompanyYearSeq()
    {
        // Default "AST-{COMPANY}-{YYYY}-{SEQ:3}" with company ABC, year 2026, seq 1 → "AST-ABC-2026-001"
        Assert.Equal("AST-ABC-2026-001", AssetTagGenerator.Render(AssetTagGenerator.DefaultFormat, 2026, 1, "ABC"));
    }

    [Theory]
    [InlineData("AST-{YYYY}-{SEQ:3}")]       // valid
    [InlineData("{SEQ:2}-X")]                 // valid, no year token
    [InlineData("ABC-{SEQ:4}")]               // valid
    public async Task SetFormatAsync_AcceptsValidFormat(string format)
    {
        await using var ctx = TestHelpers.CreateContext($"SetValid_{format.Replace("/", "_").Replace(":", "_")}");
        var gen = new AssetTagGenerator(ctx);
        await gen.SetFormatAsync(format, updatedBy: null);

        var setting = ctx.SystemSettings.FirstOrDefault(s => s.Key == AssetTagGenerator.FormatSettingKey);
        Assert.NotNull(setting);
        Assert.Equal(format, setting!.Value);
    }

    [Theory]
    [InlineData("")]            // empty
    [InlineData("   ")]         // whitespace
    [InlineData("AST-{YYYY}")]  // missing {SEQ:n}
    [InlineData("AST-{SEQ}")]   // invalid token (no width)
    public async Task SetFormatAsync_RejectsInvalidFormat(string format)
    {
        await using var ctx = TestHelpers.CreateContext($"SetInvalid_{format.Replace(" ", "s").Replace("{", "").Replace("}", "").Replace("-", "")}");
        var gen = new AssetTagGenerator(ctx);
        await Assert.ThrowsAsync<ArgumentException>(() => gen.SetFormatAsync(format, updatedBy: null));
    }

    [Fact]
    public async Task ResolveAssetTagAsync_ExplicitTag_PassedThrough()
    {
        // Explicit non-empty tag is returned as-is (no counter/DB access in this path).
        await using var ctx = TestHelpers.CreateContext(nameof(ResolveAssetTagAsync_ExplicitTag_PassedThrough));
        var gen = new AssetTagGenerator(ctx);
        var tag = await gen.ResolveAssetTagAsync("MY-CUSTOM-001", companyId: null);
        Assert.Equal("MY-CUSTOM-001", tag);
    }
}
