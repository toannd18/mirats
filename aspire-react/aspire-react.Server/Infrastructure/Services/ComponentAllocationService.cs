using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Infrastructure.Services;

/// <summary>Outcome of a component allocation/return/stock-in operation.</summary>
public record ComponentOperationResult(bool Success, string Message, string? ErrorCode = null);

/// <summary>
/// Business rules for Component stock operations, branching on <see cref="TrackingType"/>:
/// Bulk keeps the legacy quantity-pool behaviour; Serial tracks each physical unit individually.
/// Every operation writes an ActionLog in the same SaveChanges call (atomic with the change).
/// The controller wraps calls in an ambient transaction so the change + its audit log are atomic.
/// </summary>
public interface IComponentAllocationService
{
    Task<ComponentOperationResult> AllocateAsync(Guid componentId, Guid assetId, int quantity,
        string? serialNo, string? note, Guid createdById, CancellationToken ct = default);

    Task<ComponentOperationResult> ReturnAsync(Guid componentId, Guid? assetId, int quantity,
        string? serialNo, string? note, Guid createdById, CancellationToken ct = default);

    Task<ComponentOperationResult> StockInAsync(Guid componentId, IReadOnlyList<string> serialNumbers,
        string? note, Guid createdById, CancellationToken ct = default);

    Task<ComponentOperationResult> SetUnitStatusAsync(Guid unitId, ComponentUnitStatus status,
        string? note, Guid createdById, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes a serial unit that has NEVER been checked out (allocation history must stay
    /// intact — such units must be disposed instead). Decrements the parent component's Qty and
    /// writes an ActionLog, all in the same SaveChanges. Enforces company-scoping for the acting user.
    /// </summary>
    Task<ComponentOperationResult> DeleteUnitAsync(Guid unitId, Guid createdById, CancellationToken ct = default);
}

public class ComponentAllocationService : IComponentAllocationService
{
    private readonly AppDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public ComponentAllocationService(AppDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    /// <summary>
    /// Loads a component with its <c>Assignments</c>/<c>Units</c>, locking the component row
    /// <c>FOR UPDATE</c> on a real relational DB so concurrent allocation cannot overcommit the last
    /// unit (Task O-FIX, mirroring the Asset checkout pattern). On EF InMemory (which cannot translate
    /// raw SQL) it falls back to a normal load — real locking is covered by the Category=Concurrency
    /// tests against real Postgres.
    /// </summary>
    private async Task<(Component? Component, List<ComponentAssignment> Assignments, List<ComponentUnit> Units)> LoadComponentForUpdateAsync(
        Guid componentId, CancellationToken ct)
    {
        if (_context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            var component = await _context.Components
                .Include(c => c.Assignments)
                .Include(c => c.Units)
                .FirstOrDefaultAsync(c => c.Id == componentId, ct);
            if (component == null) return (null, new List<ComponentAssignment>(), new List<ComponentUnit>());
            return (component, component.Assignments.ToList(), component.Units.ToList());
        }

        var locked = await _context.Components
            .FromSqlRaw("SELECT * FROM components WHERE \"Id\" = {0} FOR UPDATE", componentId)
            .FirstOrDefaultAsync(ct);
        if (locked == null) return (null, new List<ComponentAssignment>(), new List<ComponentUnit>());

        var assignments = await _context.ComponentAssignments.Where(a => a.ComponentId == componentId).ToListAsync(ct);
        var units = await _context.ComponentUnits.Where(u => u.ComponentId == componentId).ToListAsync(ct);
        return (locked, assignments, units);
    }

    public async Task<ComponentOperationResult> AllocateAsync(Guid componentId, Guid assetId, int quantity,
        string? serialNo, string? note, Guid createdById, CancellationToken ct = default)
    {
        var (component, assignments, units) = await LoadComponentForUpdateAsync(componentId, ct);
        if (component == null)
            return new ComponentOperationResult(false, "Component not found.", "NOT_FOUND");

        var asset = await _context.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assetId, ct);
        if (asset == null)
            return new ComponentOperationResult(false, "Asset not found.", "ASSET_NOT_FOUND");

        // ─── Company scoping: a component without a company cannot be allocated at all, and a
        // component may only be allocated to an asset of the SAME company (Bulk and Serial alike).
        if (!component.CompanyId.HasValue)
            return new ComponentOperationResult(false,
                "Linh kiện chưa xác định công ty (CompanyId = null) — không thể cấp phát. Hãy bổ sung công ty trước.",
                "COMPONENT_COMPANY_REQUIRED");
        if (component.CompanyId.Value != asset.CompanyId)
        {
            var compName = (await _context.Companies.AsNoTracking().Where(x => x.Id == component.CompanyId.Value)
                .Select(x => x.Name).FirstOrDefaultAsync(ct)) ?? "?";
            var assetCompName = asset.CompanyId.HasValue
                ? (await _context.Companies.AsNoTracking().Where(x => x.Id == asset.CompanyId.Value)
                    .Select(x => x.Name).FirstOrDefaultAsync(ct)) ?? "?"
                : "chưa xác định";
            return new ComponentOperationResult(false,
                $"Linh kiện thuộc công ty \"{compName}\" không thể cấp phát cho tài sản thuộc công ty \"{assetCompName}\".",
                "COMPANY_MISMATCH");
        }

        if (component.TrackingType == TrackingType.Serial)
        {
            ComponentUnit? unit;
            if (!string.IsNullOrWhiteSpace(serialNo))
            {
                unit = units.FirstOrDefault(u => u.Status == ComponentUnitStatus.InStock && u.SerialNo == serialNo);
                if (unit == null)
                    return new ComponentOperationResult(false, $"Serial \"{serialNo}\" không tồn tại trong kho của linh kiện này.", "SERIAL_NOT_FOUND");
            }
            else
            {
                // FIFO: first in-stock unit by creation time.
                unit = units.Where(u => u.Status == ComponentUnitStatus.InStock).OrderBy(u => u.CreatedAt).FirstOrDefault();
                if (unit == null)
                    return new ComponentOperationResult(false, "Không còn linh kiện (serial) trong kho.", "INSUFFICIENT_STOCK");
            }

            var before = unit.Status.ToString();
            unit.Status = ComponentUnitStatus.Allocated;
            unit.CurrentAssetId = assetId;
            unit.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.ComponentUnit,
                ItemId = unit.Id,
                ActionType = ActionType.Checkout,
                TargetType = AssignmentTargetType.Asset,
                TargetId = assetId,
                CreatedBy = createdById,
                CompanyId = component.CompanyId,
                Note = note,
                LogMeta = JsonSerializer.Serialize(new { before, after = unit.Status.ToString(), serialNo = unit.SerialNo })
            });
        }
        else
        {
            if (quantity <= 0)
                return new ComponentOperationResult(false, "Quantity must be positive.", "INVALID_QUANTITY");

            var remaining = component.Qty - assignments.Sum(a => a.AssignedQty);
            if (quantity > remaining)
                return new ComponentOperationResult(false, $"Insufficient stock. Remaining: {remaining}", "INSUFFICIENT_STOCK");

            _context.ComponentAssignments.Add(new ComponentAssignment
            {
                ComponentId = componentId,
                AssetId = assetId,
                AssignedQty = quantity,
                Note = note
            });

            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.Component,
                ItemId = componentId,
                ActionType = ActionType.Checkout,
                TargetType = AssignmentTargetType.Asset,
                TargetId = assetId,
                CreatedBy = createdById,
                CompanyId = component.CompanyId,
                Note = note,
                LogMeta = JsonSerializer.Serialize(new { quantity })
            });
        }

        await _context.SaveChangesAsync(ct);
        return new ComponentOperationResult(true,
            component.TrackingType == TrackingType.Serial ? "Đã cấp phát linh kiện (serial)." : $"Đã cấp phát {quantity} linh kiện.");
    }

    public async Task<ComponentOperationResult> ReturnAsync(Guid componentId, Guid? assetId, int quantity,
        string? serialNo, string? note, Guid createdById, CancellationToken ct = default)
    {
        var (component, assignments, units) = await LoadComponentForUpdateAsync(componentId, ct);
        if (component == null)
            return new ComponentOperationResult(false, "Component not found.", "NOT_FOUND");

        if (component.TrackingType == TrackingType.Serial)
        {
            ComponentUnit? unit;
            if (!string.IsNullOrWhiteSpace(serialNo))
            {
                unit = units.FirstOrDefault(u => u.Status == ComponentUnitStatus.Allocated && u.SerialNo == serialNo);
                if (unit == null)
                    return new ComponentOperationResult(false, $"Serial \"{serialNo}\" không thuộc linh kiện này hoặc chưa được cấp phát.", "SERIAL_NOT_ALLOCATED");
            }
            else if (assetId.HasValue)
            {
                unit = units.FirstOrDefault(u => u.Status == ComponentUnitStatus.Allocated && u.CurrentAssetId == assetId.Value);
                if (unit == null)
                    return new ComponentOperationResult(false, "Không tìm thấy linh kiện (serial) đang cấp phát cho tài sản này.", "SERIAL_NOT_ALLOCATED");
            }
            else
            {
                return new ComponentOperationResult(false, "Cần serialNo hoặc assetId để thu hồi linh kiện Serial.", "MISSING_TARGET");
            }

            var before = unit.Status.ToString();
            // Task N: capture the real asset id BEFORE nulling it — when checking in via serialNo,
            // the request assetId is null, so TargetId must come from the unit's current asset (not
            // the request param), otherwise the audit trail loses which asset this serial was returned from.
            var returnedAssetId = unit.CurrentAssetId;
            unit.Status = ComponentUnitStatus.InStock;
            unit.CurrentAssetId = null;
            unit.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.ComponentUnit,
                ItemId = unit.Id,
                ActionType = ActionType.Checkin,
                TargetType = AssignmentTargetType.Asset,
                TargetId = returnedAssetId,
                CreatedBy = createdById,
                CompanyId = component.CompanyId,
                Note = note,
                LogMeta = JsonSerializer.Serialize(new { before, after = unit.Status.ToString(), serialNo = unit.SerialNo })
            });
        }
        else
        {
            if (quantity <= 0)
                return new ComponentOperationResult(false, "Quantity must be positive.", "INVALID_QUANTITY");
            if (!assetId.HasValue)
                return new ComponentOperationResult(false, "AssetId bắt buộc khi thu hồi linh kiện Bulk.", "MISSING_TARGET");

            var matching = assignments.Where(a => a.AssetId == assetId.Value).OrderBy(a => a.AssignedAt).ToList();
            var assignedTotal = matching.Sum(a => a.AssignedQty);
            if (quantity > assignedTotal)
                return new ComponentOperationResult(false, $"Tài sản chỉ đang giữ {assignedTotal} linh kiện này.", "INSUFFICIENT_ALLOCATION");

            var remaining = quantity;
            foreach (var assignment in matching)
            {
                if (remaining <= 0) break;
                var take = Math.Min(remaining, assignment.AssignedQty);
                assignment.AssignedQty -= take;
                remaining -= take;
                if (assignment.AssignedQty == 0)
                    _context.ComponentAssignments.Remove(assignment);
            }

            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.Component,
                ItemId = componentId,
                ActionType = ActionType.Checkin,
                TargetType = AssignmentTargetType.Asset,
                TargetId = assetId.Value,
                CreatedBy = createdById,
                CompanyId = component.CompanyId,
                Note = note,
                LogMeta = JsonSerializer.Serialize(new { quantity })
            });
        }

        await _context.SaveChangesAsync(ct);
        return new ComponentOperationResult(true,
            component.TrackingType == TrackingType.Serial ? "Đã thu hồi linh kiện (serial)." : $"Đã thu hồi {quantity} linh kiện.");
    }


    public async Task<ComponentOperationResult> StockInAsync(Guid componentId, IReadOnlyList<string> serialNumbers,
        string? note, Guid createdById, CancellationToken ct = default)
    {
        var component = await _context.Components.FirstOrDefaultAsync(c => c.Id == componentId, ct);
        if (component == null)
            return new ComponentOperationResult(false, "Component not found.", "NOT_FOUND");
        if (component.TrackingType != TrackingType.Serial)
            return new ComponentOperationResult(false, "Chỉ linh kiện Serial mới nhập kho theo serial.", "NOT_SERIAL");

        var serials = (serialNumbers ?? Array.Empty<string>())
            .Select(s => s?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (serials.Count == 0)
            return new ComponentOperationResult(false, "Danh sách serial trống.", "EMPTY_SERIALS");

        // Serial numbers are never reused — check all units including soft-deleted/disposed ones.
        var existing = await _context.ComponentUnits.IgnoreQueryFilters()
            .Where(u => u.SerialNo != null && serials.Contains(u.SerialNo))
            .Select(u => u.SerialNo!)
            .ToListAsync(ct);
        if (existing.Count > 0)
            return new ComponentOperationResult(false, $"Serial đã tồn tại trong hệ thống: {string.Join(", ", existing)}", "DUPLICATE_SERIAL");

        var nowUnspecified = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        foreach (var serial in serials)
        {
            var unit = new ComponentUnit
            {
                ComponentId = componentId,
                SerialNo = serial,
                Status = ComponentUnitStatus.InStock,
                CreatedAt = nowUnspecified,
                UpdatedAt = nowUnspecified
            };
            _context.ComponentUnits.Add(unit);

            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.ComponentUnit,
                ItemId = unit.Id,
                ActionType = ActionType.StockIn,
                CreatedBy = createdById,
                CompanyId = component.CompanyId,
                Note = note,
                LogMeta = JsonSerializer.Serialize(new { serialNo = serial, trackingType = "Serial" })
            });
        }

        // Keep the aggregate quantity in sync for backward-compatible reports/dashboards.
        component.Qty += serials.Count;
        // component.UpdatedAt is `timestamp with time zone` (safe list) → keep Kind=UTC.
        component.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        return new ComponentOperationResult(true, $"Đã nhập kho {serials.Count} serial.");
    }

    public async Task<ComponentOperationResult> SetUnitStatusAsync(Guid unitId, ComponentUnitStatus status,
        string? note, Guid createdById, CancellationToken ct = default)
    {
        var unit = await _context.ComponentUnits
            .Include(u => u.Component)
            .FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit == null)
            return new ComponentOperationResult(false, "ComponentUnit not found.", "NOT_FOUND");

        // Company scoping: a regular user may only change the status of units belonging to a
        // component in their own company. Checked here (not only in the controller) so any future
        // caller of this service method is protected too.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var unitCompanyId = unit.Component?.CompanyId;
        if (userCompanyId.HasValue && unitCompanyId.HasValue && unitCompanyId.Value != userCompanyId.Value)
            return new ComponentOperationResult(false, "ComponentUnit not found.", "NOT_FOUND");

        var before = unit.Status.ToString();
        unit.Status = status;
        unit.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        // A unit that is damaged/disposed is no longer on any asset.
        if (status != ComponentUnitStatus.Allocated)
            unit.CurrentAssetId = null;

        var actionType = status switch
        {
            ComponentUnitStatus.Damaged => ActionType.MarkDamaged,
            ComponentUnitStatus.Disposed => ActionType.Dispose,
            _ => ActionType.Update
        };

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.ComponentUnit,
            ItemId = unit.Id,
            ActionType = actionType,
            CreatedBy = createdById,
            CompanyId = unit.Component?.CompanyId,
            Note = note,
            LogMeta = JsonSerializer.Serialize(new { before, after = status.ToString(), serialNo = unit.SerialNo })
        });

        await _context.SaveChangesAsync(ct);
        return new ComponentOperationResult(true, $"�?A� c��-p nh��-t tr���ng thA�i unit thA�nh {status}.");
    }

    public async Task<ComponentOperationResult> DeleteUnitAsync(Guid unitId, Guid createdById, CancellationToken ct = default)
    {
        var unit = await _context.ComponentUnits
            .Include(u => u.Component)
            .FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit == null)
            return new ComponentOperationResult(false, "ComponentUnit not found.", "NOT_FOUND");

        // Company scoping (same as SetUnitStatusAsync): a regular user may only delete units of a
        // component in their own company. Checked here (not only in the controller) so any future
        // caller of this service method is protected too.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var unitCompanyId = unit.Component?.CompanyId;
        if (userCompanyId.HasValue && unitCompanyId.HasValue && unitCompanyId.Value != userCompanyId.Value)
            return new ComponentOperationResult(false, "ComponentUnit not found.", "NOT_FOUND");

        if (unit.DeletedAt.HasValue)
            return new ComponentOperationResult(false, "Serial đã bị xóa trước đó.", "ALREADY_DELETED");

        var hasHistory = await _context.ActionLogs.AnyAsync(l =>
            l.ItemType == ItemType.ComponentUnit && l.ItemId == unitId && l.ActionType == ActionType.Checkout, ct);
        if (hasHistory)
            return new ComponentOperationResult(false,
                "Serial này đã từng được cấp phát — hãy dùng 'Đã loại bỏ (Dispose)' thay vì xóa.",
                "COMPONENT_UNIT_HAS_ALLOCATION_HISTORY");

        unit.DeletedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        unit.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        unit.CurrentAssetId = null;
        if (unit.Component != null)
        {
            if (unit.Component.Qty > 0) unit.Component.Qty -= 1;
            unit.Component.UpdatedAt = DateTime.UtcNow;
        }

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.ComponentUnit,
            ItemId = unitId,
            ActionType = ActionType.Delete,
            CreatedBy = createdById,
            CompanyId = unit.Component?.CompanyId,
            Note = $"Xóa serial \"{unit.SerialNo}\""
        });
        await _context.SaveChangesAsync(ct);
        return new ComponentOperationResult(true, "Đã xóa serial.");
    }
}

