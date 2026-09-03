using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace aspire_react.Server.Application.Labels.Queries;

public record GeneratedLabelDto(Guid Id, string AssetTag, string Name, string QrCodeBase64);

/// <summary>
/// [Giai đoạn 3] POST /api/v1/assets/labels (extracted from LabelsController.GenerateLabels).
/// Read-only label GENERATION — no mutation, no ActionLog, no cache, no validation beyond the
/// company-scope filter and the empty-result 404 (verbatim). QR payloads embed the API base URL
/// (scheme://host/assets/{id}) which the controller passes in from HttpContext — a Query handler
/// cannot access HttpContext directly (no IHttpContextAccessor in Application by design).
/// QRCoder package moved to Application.csproj for this query (pure library, no host deps).
/// </summary>
public record GenerateLabelsQuery(IReadOnlyList<Guid> AssetIds, string BaseUrl) : IRequest<IReadOnlyList<GeneratedLabelDto>>;

public class GenerateLabelsQueryHandler : IRequestHandler<GenerateLabelsQuery, IReadOnlyList<GeneratedLabelDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GenerateLabelsQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<GeneratedLabelDto>> Handle(GenerateLabelsQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var assets = await _context.Assets
            .AsNoTracking()
            .Where(a => request.AssetIds.Contains(a.Id)
                        && (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value))
            .ToListAsync(cancellationToken);

        // Empty result → controller maps to the verbatim 404 "No assets found." body.
        var labels = assets.Select(a =>
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(
                $"{request.BaseUrl}/assets/{a.Id}",
                QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrData);
            var qrImage = qrCode.GetGraphic(10);

            return new GeneratedLabelDto(a.Id, a.AssetTag, a.Name, Convert.ToBase64String(qrImage));
        }).ToList();

        return labels;
    }
}
