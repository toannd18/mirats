using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;

namespace aspire_react.Server.Application.Licenses;

/// <summary>
/// [Giai đoạn 3 — Nặng] Shared License helpers moved verbatim from LicensesController.
/// </summary>
public static class LicenseRules
{
    /// <summary>Company-scoping: regular users only see licenses of their own company; floater
    /// licenses (no company) are visible to everyone — same convention as the rest of the system.
    /// 404 (hide existence) for out-of-scope licenses.</summary>
    public static bool IsLicenseVisible(License l, Guid? userCompanyId)
        => userCompanyId == null || l.CompanyId == null || l.CompanyId == userCompanyId.Value;

    public static int CountTargets(LicenseSeat s)
        => (s.UserId != null ? 1 : 0) + (s.AssetId != null ? 1 : 0) + (s.SystemInfoId != null ? 1 : 0);

    public static string TargetTypeLabel(LicenseSeatTargetType t) => t switch
    {
        LicenseSeatTargetType.User => "người dùng",
        LicenseSeatTargetType.Asset => "tài sản",
        _ => "hệ thống"
    };
}
