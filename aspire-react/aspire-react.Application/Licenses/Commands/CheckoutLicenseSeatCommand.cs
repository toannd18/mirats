using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Licenses;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Licenses.Commands;

public record LicenseSeatActionResult(bool Success, string Message, string? ErrorCode = null, Guid? SeatId = null);

/// <summary>
/// [Giai đoạn 3 — Nặng] POST /api/v1/licenses/{id}/checkout (extracted from LicensesController.
/// CheckoutSeat). ⚠️ TRANSACTION-BOUNDARY: strategy.ExecuteAsync → BeginTransaction → license row
/// FOR UPDATE mutex (raw SQL; InMemory fallback) → free-seat pick (explicit seatId or auto first
/// free) → assign → ActionLog in the SAME SaveChanges → Commit. Seat-picking serialized so two
/// concurrent checkouts cannot both "succeed" on the last free seat (Task O-FIX preserved).
/// Per-target validation verbatim (existence + LICENSE_COMPANY_MISMATCH + target counts).
/// </summary>
public record CheckoutLicenseSeatCommand(
    Guid LicenseId, Guid? SeatId, LicenseSeatTargetType TargetType, Guid? TargetId, string? Note, Guid CurrentUserId)
    : IRequest<LicenseSeatActionResult>;

public class CheckoutLicenseSeatCommandHandler : IRequestHandler<CheckoutLicenseSeatCommand, LicenseSeatActionResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public CheckoutLicenseSeatCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<LicenseSeatActionResult> Handle(CheckoutLicenseSeatCommand request, CancellationToken cancellationToken)
    {
        var l = await _context.Licenses.FirstOrDefaultAsync(x => x.Id == request.LicenseId && x.DeletedAt == null, cancellationToken);
        if (l == null)
            return new LicenseSeatActionResult(false, "License not found.", "NOT_FOUND");
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (!LicenseRules.IsLicenseVisible(l, userCompanyId))
            return new LicenseSeatActionResult(false, "License not found.", "NOT_FOUND");

        Guid? userId = null, assetId = null, systemInfoId = null;
        string? systemInfoName = null;
        switch (request.TargetType)
        {
            case LicenseSeatTargetType.User:
                if (!request.TargetId.HasValue) return new LicenseSeatActionResult(false, "Cần chọn người dùng nhận.", "TARGET_REQUIRED");
                var user = await _context.Users.AsNoTracking().Select(u => new { u.Id, u.CompanyId }).FirstOrDefaultAsync(u => u.Id == request.TargetId.Value, cancellationToken);
                if (user == null) return new LicenseSeatActionResult(false, "Người dùng không tồn tại.", "TARGET_NOT_FOUND");
                if (l.CompanyId.HasValue && user.CompanyId != l.CompanyId)
                    return new LicenseSeatActionResult(false, "Người dùng không thuộc cùng công ty với license.", "LICENSE_COMPANY_MISMATCH");
                userId = user.Id;
                break;
            case LicenseSeatTargetType.Asset:
                if (!request.TargetId.HasValue) return new LicenseSeatActionResult(false, "Cần chọn tài sản nhận.", "TARGET_REQUIRED");
                var asset = await _context.Assets.AsNoTracking().Select(a => new { a.Id, a.CompanyId }).FirstOrDefaultAsync(a => a.Id == request.TargetId.Value, cancellationToken);
                if (asset == null) return new LicenseSeatActionResult(false, "Tài sản không tồn tại.", "TARGET_NOT_FOUND");
                if (l.CompanyId.HasValue && asset.CompanyId != l.CompanyId)
                    return new LicenseSeatActionResult(false, "Tài sản không thuộc cùng công ty với license.", "LICENSE_COMPANY_MISMATCH");
                assetId = asset.Id;
                break;
            case LicenseSeatTargetType.SystemInfo:
                if (!request.TargetId.HasValue) return new LicenseSeatActionResult(false, "Cần chọn hệ thống nhận.", "TARGET_REQUIRED");
                var sys = await _context.SystemInfos.AsNoTracking()
                    .Select(si => new { si.Id, si.CompanyId, si.Name })
                    .FirstOrDefaultAsync(si => si.Id == request.TargetId.Value, cancellationToken);
                if (sys == null) return new LicenseSeatActionResult(false, "Hệ thống không tồn tại.", "TARGET_NOT_FOUND");
                if (l.CompanyId.HasValue && sys.CompanyId != l.CompanyId)
                    return new LicenseSeatActionResult(false, "Hệ thống không thuộc cùng công ty với license.", "LICENSE_COMPANY_MISMATCH");
                systemInfoId = sys.Id;
                systemInfoName = sys.Name;
                break;
            default:
                return new LicenseSeatActionResult(false, "Loại đối tượng nhận không hợp lệ.", "INVALID_TARGET_TYPE");
        }

        // Exactly ONE of the three target kinds must be selected.
        var targetCount = (userId != null ? 1 : 0) + (assetId != null ? 1 : 0) + (systemInfoId != null ? 1 : 0);
        if (targetCount != 1)
            return new LicenseSeatActionResult(false, "Phải chọn đúng 1 đối tượng nhận (Người dùng, Tài sản hoặc Hệ thống).", "SEAT_TARGET_AMBIGUOUS");

        // ──── Task O-FIX: serialize seat-picking under a row lock so two concurrent checkouts cannot
        // both "succeed" on the last free seat (previously both returned 200 but one silently overwrote
        // the other). The license row is locked FOR UPDATE as a mutex for seat allocation of this license.
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<LicenseSeatActionResult>(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

            var lockedLicense = _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                ? await _context.Licenses.FirstOrDefaultAsync(x => x.Id == request.LicenseId && x.DeletedAt == null, cancellationToken)
                : await _context.Licenses.FromSqlRaw(
                    "SELECT * FROM licenses WHERE \"Id\" = {0} AND \"DeletedAt\" IS NULL FOR UPDATE", request.LicenseId)
                    .FirstOrDefaultAsync(cancellationToken);
            if (lockedLicense == null)
            {
                await tx.RollbackAsync(cancellationToken);
                return new LicenseSeatActionResult(false, "License not found.", "NOT_FOUND");
            }

            // Pick a free seat (seatId optional → auto-pick the first free seat).
            LicenseSeat? seat;
            if (request.SeatId.HasValue)
            {
                seat = await _context.LicenseSeats.FirstOrDefaultAsync(s => s.Id == request.SeatId.Value && s.LicenseId == request.LicenseId, cancellationToken);
                if (seat == null)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return new LicenseSeatActionResult(false, "Seat not found.", "SEAT_NOT_FOUND");
                }
                if (LicenseRules.CountTargets(seat) > 0)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return new LicenseSeatActionResult(false, "Seat này đã được cấp phát.", "SEAT_ALREADY_ASSIGNED");
                }
            }
            else
            {
                seat = await _context.LicenseSeats
                    .Where(s => s.LicenseId == request.LicenseId && s.UserId == null && s.AssetId == null && s.SystemInfoId == null)
                    .OrderBy(s => s.SeatNumber).FirstOrDefaultAsync(cancellationToken);
                if (seat == null)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return new LicenseSeatActionResult(false, "Không còn chỗ trống trong license này.", "NO_AVAILABLE_SEATS");
                }
            }

            seat.UserId = userId;
            seat.AssetId = assetId;
            seat.SystemInfoId = systemInfoId;
            seat.AssignedAt = DateTime.UtcNow;
            seat.Note = request.Note;
            seat.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.License,
                ItemId = request.LicenseId,
                ActionType = ActionType.Checkout,
                CreatedBy = request.CurrentUserId,
                CompanyId = l.CompanyId,
                TargetType = request.TargetType switch
                {
                    LicenseSeatTargetType.User => AssignmentTargetType.User,
                    LicenseSeatTargetType.Asset => AssignmentTargetType.Asset,
                    _ => AssignmentTargetType.SystemInfo
                },
                TargetId = request.TargetId,
                TargetSystemInfoId = systemInfoId,
                TargetSystemInfoName = systemInfoName,
                Note = $"Cấp phát seat #{seat.SeatNumber} cho {LicenseRules.TargetTypeLabel(request.TargetType)}"
            });

            await _context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new LicenseSeatActionResult(true, "Seat assigned.", SeatId: seat.Id);
        });
    }
}
