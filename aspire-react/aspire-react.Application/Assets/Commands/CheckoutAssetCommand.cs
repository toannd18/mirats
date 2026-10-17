using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Assets.Commands;

public record CheckoutAssetCommand(
    Guid AssetId,
    AssignmentTargetType TargetType,
    Guid TargetId,
    string? Note,
    DateTime? CheckoutAt,
    Guid? LocationId, // Required when TargetType == SystemPosition
    Guid CurrentUserId) : IRequest<CheckoutResult>;

public record CheckoutResult(bool Success, string Message, Assignment? Assignment = null, string? ErrorCode = null);

public class CheckoutAssetCommandValidator : AbstractValidator<CheckoutAssetCommand>
{
    private readonly IApplicationDbContext _context;

    public CheckoutAssetCommandValidator(IApplicationDbContext context)
    {
        _context = context;

        RuleFor(x => x.AssetId)
            .MustAsync(async (id, ct) =>
            {
                var asset = await _context.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
                return asset != null && asset.Status != AssetStatus.Archived;
            }).WithMessage("Asset not found or has been archived.");

        RuleFor(x => x)
            .MustAsync(async (cmd, ct) =>
            {
                var asset = await _context.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == cmd.AssetId, ct);
                if (asset == null) return false;
                return asset.Status == AssetStatus.Pending;
            }).WithMessage("Asset is not in a deployable status (must be Pending).");

        RuleFor(x => x)
            .MustAsync(async (cmd, ct) =>
            {
                var asset = await _context.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == cmd.AssetId, ct);
                return asset?.CurrentAssignmentId == null;
            }).WithMessage("Asset is already checked out.");

        RuleFor(x => x.TargetType).IsInEnum();
        RuleFor(x => x.TargetId).NotEmpty();

        RuleFor(x => x.LocationId)
            .NotEmpty().When(x => x.TargetType == AssignmentTargetType.SystemPosition)
            .WithMessage("LocationId is required when checking out to a SystemPosition.");

        RuleFor(x => x)
            .MustAsync(async (cmd, ct) =>
            {
                return cmd.TargetType switch
                {
                    AssignmentTargetType.User => await _context.Users.AsNoTracking().AnyAsync(u => u.Id == cmd.TargetId && u.IsActive, ct),
                    AssignmentTargetType.Department => await _context.Departments.AsNoTracking().AnyAsync(d => d.Id == cmd.TargetId, ct),
                    AssignmentTargetType.SystemPosition => await _context.SystemPositions.AsNoTracking().AnyAsync(sp => sp.Id == cmd.TargetId, ct),
                    _ => false
                };
            }).WithMessage("Target not found or has been deleted.");
    }
}

public class CheckoutAssetCommandHandler : IRequestHandler<CheckoutAssetCommand, CheckoutResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IActionLogService _actionLogService;

    public CheckoutAssetCommandHandler(IApplicationDbContext context, IActionLogService actionLogService)
    {
        _context = context;
        _actionLogService = actionLogService;
    }

    public async Task<CheckoutResult> Handle(CheckoutAssetCommand request, CancellationToken cancellationToken)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            var asset = await _context.Assets
                .FromSqlRaw("SELECT * FROM assets WHERE \"Id\" = {0} FOR UPDATE", request.AssetId)
                .FirstOrDefaultAsync(cancellationToken);

            if (asset == null)
                return new CheckoutResult(false, "Asset not found.", ErrorCode: "ASSET_NOT_FOUND");

            if (asset.Status == AssetStatus.Archived)
                return new CheckoutResult(false, "Asset has been archived.", ErrorCode: "ASSET_ARCHIVED");

            if (asset.CurrentAssignmentId != null)
                return new CheckoutResult(false, "Asset is already checked out.", ErrorCode: "ASSET_ALREADY_CHECKED_OUT");

            if (asset.Status != AssetStatus.Pending)
                return new CheckoutResult(false, "Asset is not available for checkout.", ErrorCode: "ASSET_NOT_DEPLOYABLE");

            // ──── Company Isolation ────
            if (asset.CompanyId.HasValue)
            {
                Guid? targetCompanyId = request.TargetType switch
                {
                    AssignmentTargetType.User => await _context.Users.Where(u => u.Id == request.TargetId).Select(u => u.CompanyId).FirstOrDefaultAsync(cancellationToken),
                    AssignmentTargetType.Department => await _context.Departments.Where(d => d.Id == request.TargetId).Select(d => d.CompanyId).FirstOrDefaultAsync(cancellationToken),
                    AssignmentTargetType.SystemPosition => await _context.SystemPositions.Include(sp => sp.SystemInfo).Where(sp => sp.Id == request.TargetId).Select(sp => sp.SystemInfo.CompanyId).FirstOrDefaultAsync(cancellationToken),
                    _ => null
                };

                if (targetCompanyId != asset.CompanyId)
                    return new CheckoutResult(false, "Đối tượng nhận không thuộc cùng công ty với tài sản này.", ErrorCode: "COMPANY_MISMATCH");
            }

            // ──── SystemPositionId + LocationId logic ────
            if (request.TargetType == AssignmentTargetType.SystemPosition)
            {
                if (!request.LocationId.HasValue)
                    return new CheckoutResult(false, "LocationId is required when checking out to a SystemPosition.", ErrorCode: "LOCATION_REQUIRED");

                asset.SystemPositionId = request.TargetId;
                asset.LocationId = request.LocationId.Value;
            }
            else
            {
                asset.SystemPositionId = null;
                asset.LocationId = null; // User/Department don't carry location
            }

            // Snapshot for audit
            var oldSnapshot = new Dictionary<string, object?>
            {
                ["current_assignment_id"] = asset.CurrentAssignmentId,
                ["location_id"] = asset.LocationId,
                ["system_position_id"] = asset.SystemPositionId,
                ["status"] = asset.Status.ToString(),
                ["checkout_counter"] = asset.CheckoutCounter
            };

            // [LL-2] Mốc thời gian của lần checkout: dùng CHUNG cho Assignment.AssignedAt và
            // StartedAt của lý lịch lắp đặt (một mốc duy nhất — không gọi DateTime.UtcNow hai lần).
            var startedAt = request.CheckoutAt ?? DateTime.UtcNow;

            // Create Assignment
            var assignment = new Assignment
            {
                AssetId = request.AssetId,
                TargetType = request.TargetType,
                TargetId = request.TargetId,
                AssignedById = request.CurrentUserId,
                AssignedAt = startedAt,
                Note = request.Note
            };
            _context.Assignments.Add(assignment);
            await _context.SaveChangesAsync(cancellationToken);

            // [LL-2] Checkout tới SystemPosition → MỞ khoảng lắp đặt mới (Source=Api, StartedAt =
            // cùng mốc với Assignment; denormalize tên vị trí + tên hệ thống; LoggedById/LoggedByName).
            // Checkout tới User/Department KHÔNG ghi lý lịch. Bất biến "SystemPositionId != null ⟺
            // đúng 1 dòng mở" được kiểm tra sau SaveChanges (xem IsConsistentAsync bên dưới).
            if (request.TargetType == AssignmentTargetType.SystemPosition)
            {
                var position = await _context.SystemPositions.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == request.TargetId, cancellationToken);
                if (position == null)
                    return new CheckoutResult(false, "Target not found or has been deleted.", ErrorCode: "TARGET_NOT_FOUND");

                var systemInfoName = await _context.SystemInfos.AsNoTracking()
                    .Where(s => s.Id == position.SystemInfoId)
                    .Select(s => s.Name)
                    .FirstOrDefaultAsync(cancellationToken);
                var actorName = await InstallationLedger.ResolveActorNameAsync(_context, request.CurrentUserId, cancellationToken);

                InstallationLedger.Open(_context, asset.Id, position, systemInfoName, startedAt,
                    request.CurrentUserId, actorName, AssetInstallationSource.Api);
            }

            // Update Asset
            asset.CurrentAssignmentId = assignment.Id;
            asset.Status = AssetStatus.Deployed;
            asset.CheckoutCounter++;
            asset.LastCheckout = assignment.AssignedAt;

            _actionLogService.LogAction(
                itemType: ItemType.Asset,
                itemId: request.AssetId,
                actionType: ActionType.Checkout,
                loggedByUserId: request.CurrentUserId,
                targetType: request.TargetType,
                targetId: request.TargetId,
                locationId: asset.LocationId,
                companyId: asset.CompanyId,
                note: request.Note,
                logMeta: JsonSerializer.Serialize(new
                {
                    changes = new Dictionary<string, object?>
                    {
                        ["current_assignment_id"] = new { old = oldSnapshot["current_assignment_id"], @new = assignment.Id.ToString() },
                        ["status"] = new { old = oldSnapshot["status"], @new = AssetStatus.Deployed.ToString() },
                        ["location_id"] = new { old = oldSnapshot["location_id"]?.ToString(), @new = asset.LocationId?.ToString() },
                        ["system_position_id"] = new { old = oldSnapshot["system_position_id"]?.ToString(), @new = asset.SystemPositionId?.ToString() }
                    }
                }));

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg
                && pg.SqlState == Npgsql.PostgresErrorCodes.ExclusionViolation)
            {
                // [LL-2] EX_asset_installations_no_overlap (SQLSTATE 23P01): thiết bị đã có một khoảng
                // lắp đặt chồng với khoảng mới (VD checkout lùi ngày về trước mốc tháo của lần trước).
                // Trả 409 INSTALLATION_PERIOD_OVERLAP — KHÔNG để lọt 500 thô.
                await transaction.RollbackAsync(cancellationToken);
                return new CheckoutResult(false,
                    "Khoảng lắp đặt bị chồng với lý lịch đã có của thiết bị — kiểm tra lại thời điểm checkout.",
                    ErrorCode: InstallationLedger.Overlap);
            }

            // [LL-2] Bất biến: SystemPositionId != null ⟺ ĐÚNG 1 dòng mở (chưa superseded) trỏ đúng vị trí.
            // Kiểm tra sau SaveChanges, trước Commit → phá bất biến thì rollback, không ghi gì.
            if (!await InstallationLedger.IsConsistentAsync(_context, asset, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CheckoutResult(false,
                    "Lý lịch lắp đặt của thiết bị không nhất quán với trạng thái hiện tại — thao tác đã được hoàn tác.",
                    ErrorCode: InstallationLedger.Inconsistent);
            }

            await transaction.CommitAsync(cancellationToken);

            return new CheckoutResult(true, "Asset checked out successfully.", assignment);
        });
    }
}