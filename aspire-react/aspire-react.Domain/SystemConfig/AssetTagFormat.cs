using System.Text.RegularExpressions;

namespace aspire_react.Server.Domain.SystemConfig;

/// <summary>
/// [FIX B 2026-10-02] Asset-tag format contract — constants + validation + rendering.
///
/// WHY it lives in Domain: the format used to be defined entirely inside
/// <c>Infrastructure.Services.AssetTagGenerator</c> (constants + static ValidateFormat/Render).
/// The SystemConfig endpoints migrated to MediatR, and an Application command handler is not allowed
/// to reference an Infrastructure type, so the contract moved here — ONE source of truth consumed by
/// BOTH the Infrastructure generator (which renders tags) and the Application command (which
/// persists a new format with an atomic ActionLog). AssetTagGenerator now forwards to this class so
/// existing callers keep working unchanged.
///
/// Format syntax: a free string with tokens <c>{COMPANY}</c> (company Code, "NOCO" for company-less),
/// <c>{YYYY}</c> (4-digit year) and <c>{SEQ:n}</c> (sequence padded to n digits, n=1..9); any other
/// characters are literal. Example: "AST-{COMPANY}-{YYYY}-{SEQ:3}" → "AST-ABC-2026-001".
/// </summary>
public static class AssetTagFormat
{
    public const string DefaultFormat = "AST-{COMPANY}-{YYYY}-{SEQ:3}";
    public const string SettingKey = "AssetTagFormat";

    /// <summary>Description stamped on the SystemSetting row when it is first created (shared by
    /// AssetTagGenerator.SetFormatAsync and the MediatR write path so the text never diverges).</summary>
    public const string Description = "Format tự sinh Mã tài sản (Asset Tag). Hỗ trợ {COMPANY} (mã công ty, NOCO nếu không có), {YYYY} (năm 4 số) và {SEQ:n} (số thứ tự đệm n chữ số). Nên giữ {COMPANY} để mã unique toàn hệ thống.";

    /// <summary>Reserved code for company-less (floater) assets.</summary>
    public const string NoCompanyCode = "NOCO";

    private static readonly Regex SeqTokenRegex = new(@"\{SEQ:(\d)\}", RegexOptions.Compiled);
    private static readonly Regex CompanyTokenRegex = new(@"\{COMPANY\}", RegexOptions.Compiled);

    /// <summary>
    /// Validates an asset-tag format candidate (SEC-FIX A1: extracted so the write path validates
    /// identically to <c>AssetTagGenerator.SetFormatAsync</c>). Throws
    /// <see cref="ArgumentException"/> with the same messages the generator has always used.
    /// </summary>
    public static void Validate(string? format)
    {
        var trimmed = format?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) throw new ArgumentException("Format không được để trống.");
        if (!SeqTokenRegex.IsMatch(trimmed))
            throw new ArgumentException("Format phải chứa token {SEQ:n} (VD {SEQ:3}).");
    }

    /// <summary>Renders the format with the given year, sequence and company code.</summary>
    public static string Render(string format, int year, long seq, string companyCode)
    {
        var result = format.Replace("{YYYY}", year.ToString("D4"));
        result = CompanyTokenRegex.Replace(result, companyCode);
        return SeqTokenRegex.Replace(result, m =>
        {
            var width = int.Parse(m.Groups[1].Value);
            return seq.ToString($"D{width}");
        });
    }
}
