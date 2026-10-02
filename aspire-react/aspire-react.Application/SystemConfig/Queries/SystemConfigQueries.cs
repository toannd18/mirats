using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.SystemConfig.Queries;

/// <summary>
/// [FIX B 2026-10-02] GET /api/v1/system/config/asset-tag-format — moved out of
/// SystemConfigController (the last controller without IMediator). Any authenticated user may read
/// it so the asset create form can show the hint; the PUT is gated by policy `system.config`.
/// </summary>
public record GetAssetTagFormatQuery : IRequest<string>;

public class GetAssetTagFormatQueryHandler : IRequestHandler<GetAssetTagFormatQuery, string>
{
    private readonly IAssetTagGenerator _assetTagGenerator;

    public GetAssetTagFormatQueryHandler(IAssetTagGenerator assetTagGenerator)
    {
        _assetTagGenerator = assetTagGenerator;
    }

    public Task<string> Handle(GetAssetTagFormatQuery request, CancellationToken cancellationToken)
        => _assetTagGenerator.GetFormatAsync(cancellationToken);
}

/// <summary>
/// [FIX B 2026-10-02] GET /api/v1/system/config/passkeys-enabled — the AUTH Phase 3 flag
/// (SystemSetting `auth.passkeys.enabled`). Readable by any authenticated user (the account page
/// uses it to gate the passkey registration UI).
/// </summary>
public record GetPasskeysEnabledQuery : IRequest<bool>;

public class GetPasskeysEnabledQueryHandler : IRequestHandler<GetPasskeysEnabledQuery, bool>
{
    private readonly IApplicationDbContext _context;

    public GetPasskeysEnabledQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(GetPasskeysEnabledQuery request, CancellationToken cancellationToken)
    {
        var setting = await _context.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == IWebAuthnService.PasskeysEnabledSettingKey, cancellationToken);
        return string.Equals(setting?.Value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
