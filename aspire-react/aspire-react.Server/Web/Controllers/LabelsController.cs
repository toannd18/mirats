using System.Text.Json;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace aspire_react.Server.Web.Controllers;

[ApiController]
[Route("api/v1/assets")]
public class LabelsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    public LabelsController(AppDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    [HttpPost("labels")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GenerateLabels([FromBody] GenerateLabelsRequest request)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var assets = await _context.Assets
            .AsNoTracking()
            .Where(a => request.AssetIds.Contains(a.Id)
                        && (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value))
            .ToListAsync();

        if (assets.Count == 0)
            return NotFound(new { status = "error", message = "No assets found." });

        // Generate QR codes as base64 PNG images
        var labels = assets.Select(a =>
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(
                $"{Request.Scheme}://{Request.Host}/assets/{a.Id}",
                QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrData);
            var qrImage = qrCode.GetGraphic(10);

            return new
            {
                a.Id,
                a.AssetTag,
                a.Name,
                QrCodeBase64 = Convert.ToBase64String(qrImage)
            };
        }).ToList();

        return Ok(new { status = "success", data = labels });
    }
}

public record GenerateLabelsRequest(List<Guid> AssetIds);