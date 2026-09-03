using System.Text.RegularExpressions;

namespace aspire_react.Server.Application.SystemInfos;

/// <summary>
/// [Giai đoạn 3] Shared Code rules for SystemInfo/SystemPosition (moved verbatim from
/// SystemInfoController): Code format XXX(X)-YYYY-ZZZ — 3-4 uppercase letters prefix,
/// 4-digit year, 3-digit per-year sequence.
/// </summary>
public static class SystemCodeRules
{
    public static readonly Regex CodeRegex = new(@"^[A-Z]{3,4}-\d{4}-\d{3}$", RegexOptions.Compiled);

    public static string? Normalize(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
}
