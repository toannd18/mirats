using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.MaintenanceTemplates.Commands;

// ─── Shared item/param helpers (ported verbatim from controller privates) ───

internal enum EditableVersionStatus { Ok, NotFound, Frozen }

internal static class MaintenanceTemplateItems
{
    /// <summary>
    /// Guard chung cho mọi thao tác ghi vào nội dung version: có campaign → Frozen (TEMPLATE_VERSION_IN_USE).
    /// Distinguishes "not found" from "frozen" exactly like the controller did (re-check existence).
    /// </summary>
    internal static async Task<(EditableVersionStatus Status, MaintenanceChecklistTemplateVersion? Version)> GetEditableVersionAsync(
        IApplicationDbContext context, MaintenanceChecklistTemplate template, Guid versionId)
    {
        var version = await MaintenanceTemplateAccess.GetVersionOfTemplateAsync(context, template, versionId);
        if (version == null) return (EditableVersionStatus.NotFound, null);
        if (await MaintenanceTemplateAccess.VersionHasCampaignsAsync(context, versionId))
            return (EditableVersionStatus.Frozen, version);
        return (EditableVersionStatus.Ok, version);
    }

    /// <summary>
    /// [MC-7b] Mọi vị trí được khai báo phải TỒN TẠI và THUỘC ĐÚNG SystemInfo của template
    /// (per-system từ MC-1) — nếu không, khớp Position sẽ không bao giờ đúng → trả INVALID_POSITION.
    /// null/[] = universal (không validate, không tạo row nào) — xử lý ở caller.
    /// </summary>
    internal static async Task<string?> ValidatePositionsErrorAsync(
        IApplicationDbContext context, MaintenanceChecklistTemplate template, Guid[]? positionIds)
    {
        if (positionIds == null) return null;
        var distinct = positionIds.Distinct().ToArray();
        if (distinct.Length == 0) return null;
        var found = await context.SystemPositions.AsNoTracking()
            .Where(p => distinct.Contains(p.Id) && p.SystemInfoId == template.SystemInfoId)
            .Select(p => p.Id)
            .ToListAsync();
        if (found.Count != distinct.Length)
            return "INVALID_POSITION";
        return null;
    }

    /// <summary>[MC-7b] Thay toàn bộ danh sách vị trí của item. PositionIds null = không đụng (patch); [] = universal.</summary>
    internal static async Task ReplaceItemPositionsAsync(
        IApplicationDbContext context, MaintenanceChecklistItem item, Guid[]? positionIds)
    {
        if (positionIds == null) return;
        var existing = await context.MaintenanceChecklistItemPositions
            .Where(ip => ip.ItemId == item.Id)
            .ToListAsync();
        context.MaintenanceChecklistItemPositions.RemoveRange(existing);
        foreach (var pid in positionIds.Distinct())
        {
            context.MaintenanceChecklistItemPositions.Add(new MaintenanceChecklistItemPosition
            {
                ItemId = item.Id,
                SystemPositionId = pid
            });
        }
    }

    internal static string FrozenMessage(MaintenanceChecklistTemplateVersion version)
        => $"Version {version.VersionNumber} đã có đợt bảo dưỡng tham chiếu — nội dung bất biến. Hãy tạo version mới.";
}

// ─── ADD ITEM ───

public record AddChecklistItemResult(
    bool Success, string Message, string? ErrorCode = null,
    Guid? ItemId = null, int? Order = null, string? Name = null, int? CycleMonths = null,
    string? ToolsRequired = null, string? Instruction = null, Guid[]? PositionIds = null);

public record AddChecklistItemCommand(
    Guid TemplateId, Guid VersionId, int? Order, string? Name, int? CycleMonths,
    string? ToolsRequired, string? Instruction, Guid[]? PositionIds, Guid CurrentUserId)
    : IRequest<AddChecklistItemResult>;

public class AddChecklistItemCommandHandler : IRequestHandler<AddChecklistItemCommand, AddChecklistItemResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public AddChecklistItemCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<AddChecklistItemResult> Handle(AddChecklistItemCommand request, CancellationToken cancellationToken)
    {
        var t = await MaintenanceTemplateAccess.GetVisibleTemplateAsync(_context, _companyScope, request.TemplateId);
        if (t == null)
            return new AddChecklistItemResult(false, "Not found.", "NOT_FOUND");
        var (status, version) = await MaintenanceTemplateItems.GetEditableVersionAsync(_context, t, request.VersionId);
        if (status == EditableVersionStatus.NotFound)
            return new AddChecklistItemResult(false, "Version not found.", "VERSION_NOT_FOUND");
        if (status == EditableVersionStatus.Frozen || version == null)
            return new AddChecklistItemResult(false, MaintenanceTemplateItems.FrozenMessage(version!), "TEMPLATE_VERSION_IN_USE");

        if (string.IsNullOrWhiteSpace(request.Name))
            return new AddChecklistItemResult(false, "Tên hạng mục kiểm tra là bắt buộc.", "ITEM_NAME_REQUIRED");
        if (request.CycleMonths.HasValue && request.CycleMonths.Value <= 0)
            return new AddChecklistItemResult(false, "Chu kỳ (tháng) phải lớn hơn 0.", "INVALID_CYCLE_MONTHS");
        if (await MaintenanceTemplateItems.ValidatePositionsErrorAsync(_context, t, request.PositionIds) is { } posErr)
            return new AddChecklistItemResult(false, "Có vị trí không tồn tại hoặc không thuộc hệ thống của template.", posErr);

        var order = request.Order ?? (await _context.MaintenanceChecklistItems.AsNoTracking()
            .Where(i => i.TemplateVersionId == request.VersionId)
            .MaxAsync(i => (int?)i.Order, cancellationToken) ?? 0) + 1;
        if (order <= 0)
            return new AddChecklistItemResult(false, "Thứ tự (Order) phải lớn hơn 0.", "INVALID_ORDER");
        if (await _context.MaintenanceChecklistItems.AnyAsync(i => i.TemplateVersionId == request.VersionId && i.Order == order, cancellationToken))
            return new AddChecklistItemResult(false, $"Thứ tự {order} đã có hạng mục khác sử dụng.", "ITEM_ORDER_TAKEN");

        var item = new MaintenanceChecklistItem
        {
            TemplateVersionId = request.VersionId,
            Order = order,
            Name = request.Name.Trim(),
            CycleMonths = request.CycleMonths ?? 12,
            ToolsRequired = request.ToolsRequired,
            Instruction = request.Instruction
        };
        // [MC-7b] Khai báo phạm vi vị trí ngay lúc tạo (null/[] = universal → không tạo dòng nào).
        foreach (var pid in request.PositionIds?.Distinct() ?? Array.Empty<Guid>())
        {
            item.Positions.Add(new MaintenanceChecklistItemPosition { SystemPositionId = pid });
        }
        _context.MaintenanceChecklistItems.Add(item);
        await _context.SaveChangesAsync(cancellationToken);

        _actionLogService.Log(MaintenanceTemplateLogging.Build(
            ActionType.Create, t, request.CurrentUserId,
            $"Thêm hạng mục \"{item.Name}\" vào version {version.VersionNumber}",
            new { scope = "item", versionId = request.VersionId, versionNumber = version.VersionNumber, itemId = item.Id, Order = item.Order, positionIds = request.PositionIds ?? Array.Empty<Guid>() }));
        await _context.SaveChangesAsync(cancellationToken);

        return new AddChecklistItemResult(true, "Created.",
            ItemId: item.Id, Order: item.Order, Name: item.Name, CycleMonths: item.CycleMonths,
            ToolsRequired: item.ToolsRequired, Instruction: item.Instruction,
            PositionIds: request.PositionIds ?? Array.Empty<Guid>());
    }
}

// ─── UPDATE ITEM ───

public record UpdateChecklistItemResult(bool Success, string Message, string? ErrorCode = null);

public record UpdateChecklistItemCommand(
    Guid TemplateId, Guid VersionId, Guid ItemId, int? Order, string? Name, int? CycleMonths,
    string? ToolsRequired, string? Instruction, Guid[]? PositionIds, Guid CurrentUserId)
    : IRequest<UpdateChecklistItemResult>;

public class UpdateChecklistItemCommandHandler : IRequestHandler<UpdateChecklistItemCommand, UpdateChecklistItemResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public UpdateChecklistItemCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<UpdateChecklistItemResult> Handle(UpdateChecklistItemCommand request, CancellationToken cancellationToken)
    {
        var t = await MaintenanceTemplateAccess.GetVisibleTemplateAsync(_context, _companyScope, request.TemplateId);
        if (t == null)
            return new UpdateChecklistItemResult(false, "Not found.", "NOT_FOUND");
        var version = await MaintenanceTemplateAccess.GetVersionOfTemplateAsync(_context, t, request.VersionId);
        if (version == null)
            return new UpdateChecklistItemResult(false, "Version not found.", "VERSION_NOT_FOUND");
        var item = await _context.MaintenanceChecklistItems
            .FirstOrDefaultAsync(i => i.Id == request.ItemId && i.TemplateVersionId == request.VersionId, cancellationToken);
        if (item == null)
            return new UpdateChecklistItemResult(false, "Item not found.", "ITEM_NOT_FOUND");
        if (await MaintenanceTemplateAccess.VersionHasCampaignsAsync(_context, request.VersionId))
            return new UpdateChecklistItemResult(false, MaintenanceTemplateItems.FrozenMessage(version), "TEMPLATE_VERSION_IN_USE");

        if (request.CycleMonths.HasValue && request.CycleMonths.Value <= 0)
            return new UpdateChecklistItemResult(false, "Chu kỳ (tháng) phải lớn hơn 0.", "INVALID_CYCLE_MONTHS");
        // [MC-7b] Nếu PositionIds ĐƯỢC GỬI (null/[] = universal, danh sách = khai báo) → validate + thay toàn bộ.
        if (request.PositionIds != null)
        {
            if (await MaintenanceTemplateItems.ValidatePositionsErrorAsync(_context, t, request.PositionIds) is { } posErr)
                return new UpdateChecklistItemResult(false, "Có vị trí không tồn tại hoặc không thuộc hệ thống của template.", posErr);
            await MaintenanceTemplateItems.ReplaceItemPositionsAsync(_context, item, request.PositionIds);
        }

        int? newOrder = null;
        if (request.Order.HasValue && request.Order.Value != item.Order)
        {
            if (request.Order.Value <= 0)
                return new UpdateChecklistItemResult(false, "Thứ tự (Order) phải lớn hơn 0.", "INVALID_ORDER");
            if (await _context.MaintenanceChecklistItems.AnyAsync(
                    i => i.TemplateVersionId == request.VersionId && i.Order == request.Order.Value && i.Id != request.ItemId, cancellationToken))
                return new UpdateChecklistItemResult(false, $"Thứ tự {request.Order.Value} đã có hạng mục khác sử dụng.", "ITEM_ORDER_TAKEN");
            newOrder = request.Order.Value;
        }

        // Patch semantics: absent fields NEVER overwrite real data.
        var before = new { item.Order, item.Name, item.CycleMonths, item.ToolsRequired, item.Instruction };
        if (newOrder.HasValue) item.Order = newOrder.Value;
        if (!string.IsNullOrWhiteSpace(request.Name)) item.Name = request.Name.Trim();
        if (request.CycleMonths.HasValue) item.CycleMonths = request.CycleMonths.Value;
        if (request.ToolsRequired is not null) item.ToolsRequired = request.ToolsRequired;
        if (request.Instruction is not null) item.Instruction = request.Instruction;
        await _context.SaveChangesAsync(cancellationToken);

        _actionLogService.Log(MaintenanceTemplateLogging.Build(
            ActionType.Update, t, request.CurrentUserId,
            $"Sửa hạng mục \"{item.Name}\" (version {version.VersionNumber})", new
            {
                scope = "item",
                versionId = request.VersionId,
                versionNumber = version.VersionNumber,
                itemId = request.ItemId,
                changes = new
                {
                    order = new { old = before.Order, @new = item.Order },
                    name = new { old = before.Name, @new = item.Name },
                    cycleMonths = new { old = before.CycleMonths, @new = item.CycleMonths },
                    toolsRequired = new { old = before.ToolsRequired, @new = item.ToolsRequired },
                    instruction = new { old = before.Instruction, @new = item.Instruction }
                }
            }));
        await _context.SaveChangesAsync(cancellationToken);

        return new UpdateChecklistItemResult(true, "Item updated.");
    }
}

// ─── DELETE ITEM ───

public record DeleteChecklistItemResult(bool Success, string Message, string? ErrorCode = null);

public record DeleteChecklistItemCommand(Guid TemplateId, Guid VersionId, Guid ItemId, Guid CurrentUserId)
    : IRequest<DeleteChecklistItemResult>;

public class DeleteChecklistItemCommandHandler : IRequestHandler<DeleteChecklistItemCommand, DeleteChecklistItemResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public DeleteChecklistItemCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<DeleteChecklistItemResult> Handle(DeleteChecklistItemCommand request, CancellationToken cancellationToken)
    {
        var t = await MaintenanceTemplateAccess.GetVisibleTemplateAsync(_context, _companyScope, request.TemplateId);
        if (t == null)
            return new DeleteChecklistItemResult(false, "Not found.", "NOT_FOUND");
        var version = await MaintenanceTemplateAccess.GetVersionOfTemplateAsync(_context, t, request.VersionId);
        if (version == null)
            return new DeleteChecklistItemResult(false, "Version not found.", "VERSION_NOT_FOUND");
        var item = await _context.MaintenanceChecklistItems
            .FirstOrDefaultAsync(i => i.Id == request.ItemId && i.TemplateVersionId == request.VersionId, cancellationToken);
        if (item == null)
            return new DeleteChecklistItemResult(false, "Item not found.", "ITEM_NOT_FOUND");
        if (await MaintenanceTemplateAccess.VersionHasCampaignsAsync(_context, request.VersionId))
            return new DeleteChecklistItemResult(false, MaintenanceTemplateItems.FrozenMessage(version), "TEMPLATE_VERSION_IN_USE");

        var name = item.Name;
        _context.MaintenanceChecklistItems.Remove(item);
        await _context.SaveChangesAsync(cancellationToken);

        _actionLogService.Log(MaintenanceTemplateLogging.Build(
            ActionType.Delete, t, request.CurrentUserId,
            $"Xóa hạng mục \"{name}\" khỏi version {version.VersionNumber}",
            new { scope = "item", versionId = request.VersionId, versionNumber = version.VersionNumber, itemId = request.ItemId }));
        await _context.SaveChangesAsync(cancellationToken);
        return new DeleteChecklistItemResult(true, "Item deleted.");
    }
}

// ─── STANDARD PARAMS ([MC-8] nested trong item, [MC-10] ngưỡng cấu trúc bắt buộc) ───

public record AddStandardParamResult(
    bool Success, string Message, string? ErrorCode = null,
    Guid? ParamId = null, string? ParamName = null, string? NominalValue = null,
    MaintenanceThresholdOperator? ThresholdOperator = null, decimal? ThresholdValue = null,
    string? CheckMethod = null, string? Unit = null, Guid? ItemId = null);

public record AddStandardParamCommand(
    Guid TemplateId, Guid VersionId, Guid ItemId,
    string? ParamName, string? NominalValue, MaintenanceThresholdOperator? ThresholdOperator,
    decimal? ThresholdValue, string? CheckMethod, string? Unit, Guid CurrentUserId)
    : IRequest<AddStandardParamResult>;

public class AddStandardParamCommandHandler : IRequestHandler<AddStandardParamCommand, AddStandardParamResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public AddStandardParamCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<AddStandardParamResult> Handle(AddStandardParamCommand request, CancellationToken cancellationToken)
    {
        var t = await MaintenanceTemplateAccess.GetVisibleTemplateAsync(_context, _companyScope, request.TemplateId);
        if (t == null)
            return new AddStandardParamResult(false, "Not found.", "NOT_FOUND");
        var (status, version) = await MaintenanceTemplateItems.GetEditableVersionAsync(_context, t, request.VersionId);
        if (status == EditableVersionStatus.NotFound)
            return new AddStandardParamResult(false, "Version not found.", "VERSION_NOT_FOUND");
        if (status == EditableVersionStatus.Frozen || version == null)
            return new AddStandardParamResult(false, MaintenanceTemplateItems.FrozenMessage(version!), "TEMPLATE_VERSION_IN_USE");
        var item = await _context.MaintenanceChecklistItems
            .FirstOrDefaultAsync(i => i.Id == request.ItemId && i.TemplateVersionId == request.VersionId, cancellationToken);
        if (item == null)
            return new AddStandardParamResult(false, "Item not found.", "ITEM_NOT_FOUND");

        if (string.IsNullOrWhiteSpace(request.ParamName))
            return new AddStandardParamResult(false, "Tên thông số là bắt buộc.", "PARAM_REQUIRED");

        // [MC-10] Ngưỡng BẮT BUỘC cấu trúc (Operator + Value) — máy tự suy Đạt/Không đạt.
        if (!request.ThresholdOperator.HasValue)
            return new AddStandardParamResult(false, "Toán tử ngưỡng (ThresholdOperator) là bắt buộc.", "THRESHOLD_OPERATOR_REQUIRED");
        if (!request.ThresholdValue.HasValue)
            return new AddStandardParamResult(false, "Giá trị ngưỡng (ThresholdValue) là bắt buộc.", "THRESHOLD_VALUE_REQUIRED");

        var param = new MaintenanceStandardParam
        {
            ChecklistItemId = request.ItemId,
            ParamName = request.ParamName.Trim(),
            NominalValue = request.NominalValue,
            ThresholdOperator = request.ThresholdOperator.Value,
            ThresholdValue = request.ThresholdValue.Value,
            CheckMethod = request.CheckMethod,
            Unit = request.Unit
        };
        _context.MaintenanceStandardParams.Add(param);
        await _context.SaveChangesAsync(cancellationToken);

        _actionLogService.Log(MaintenanceTemplateLogging.Build(
            ActionType.Create, t, request.CurrentUserId,
            $"Thêm tiêu chuẩn \"{param.ParamName}\" vào hạng mục \"{item.Name}\" (version {version.VersionNumber})",
            new { scope = "param", versionId = request.VersionId, versionNumber = version.VersionNumber, itemId = request.ItemId, paramId = param.Id }));
        await _context.SaveChangesAsync(cancellationToken);

        return new AddStandardParamResult(true, "Created.",
            ParamId: param.Id, ParamName: param.ParamName, NominalValue: param.NominalValue,
            ThresholdOperator: param.ThresholdOperator, ThresholdValue: param.ThresholdValue,
            CheckMethod: param.CheckMethod, Unit: param.Unit, ItemId: request.ItemId);
    }
}

public record UpdateStandardParamResult(bool Success, string Message, string? ErrorCode = null);

public record UpdateStandardParamCommand(
    Guid TemplateId, Guid VersionId, Guid ItemId, Guid ParamId,
    string? ParamName, string? NominalValue, MaintenanceThresholdOperator? ThresholdOperator,
    decimal? ThresholdValue, string? CheckMethod, string? Unit, Guid CurrentUserId)
    : IRequest<UpdateStandardParamResult>;

public class UpdateStandardParamCommandHandler : IRequestHandler<UpdateStandardParamCommand, UpdateStandardParamResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public UpdateStandardParamCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<UpdateStandardParamResult> Handle(UpdateStandardParamCommand request, CancellationToken cancellationToken)
    {
        var t = await MaintenanceTemplateAccess.GetVisibleTemplateAsync(_context, _companyScope, request.TemplateId);
        if (t == null)
            return new UpdateStandardParamResult(false, "Not found.", "NOT_FOUND");
        var version = await MaintenanceTemplateAccess.GetVersionOfTemplateAsync(_context, t, request.VersionId);
        if (version == null)
            return new UpdateStandardParamResult(false, "Version not found.", "VERSION_NOT_FOUND");
        var param = await _context.MaintenanceStandardParams
            .FirstOrDefaultAsync(p => p.Id == request.ParamId && p.ChecklistItemId == request.ItemId, cancellationToken);
        if (param == null)
            return new UpdateStandardParamResult(false, "Param not found.", "PARAM_NOT_FOUND");
        if (await MaintenanceTemplateAccess.VersionHasCampaignsAsync(_context, request.VersionId))
            return new UpdateStandardParamResult(false, MaintenanceTemplateItems.FrozenMessage(version), "TEMPLATE_VERSION_IN_USE");

        var before = new { param.ParamName, param.NominalValue, param.ThresholdOperator, param.ThresholdValue, param.CheckMethod, param.Unit };
        if (!string.IsNullOrWhiteSpace(request.ParamName)) param.ParamName = request.ParamName.Trim();
        if (request.NominalValue is not null) param.NominalValue = request.NominalValue;
        // [MC-10] Patch-aware nhưng luôn theo cặp: Operator+Value (cả 2 hoặc không đổi).
        if (request.ThresholdOperator.HasValue) param.ThresholdOperator = request.ThresholdOperator.Value;
        if (request.ThresholdValue.HasValue) param.ThresholdValue = request.ThresholdValue.Value;
        if (request.CheckMethod is not null) param.CheckMethod = request.CheckMethod;
        if (request.Unit is not null) param.Unit = request.Unit;
        await _context.SaveChangesAsync(cancellationToken);

        _actionLogService.Log(MaintenanceTemplateLogging.Build(
            ActionType.Update, t, request.CurrentUserId,
            $"Sửa tiêu chuẩn \"{param.ParamName}\" (version {version.VersionNumber})", new
            {
                scope = "param",
                versionId = request.VersionId,
                versionNumber = version.VersionNumber,
                itemId = request.ItemId,
                paramId = request.ParamId,
                changes = new
                {
                    paramName = new { old = before.ParamName, @new = param.ParamName },
                    nominalValue = new { old = before.NominalValue, @new = param.NominalValue },
                    thresholdOperator = new { old = before.ThresholdOperator.ToString(), @new = param.ThresholdOperator.ToString() },
                    thresholdValue = new { old = before.ThresholdValue, @new = param.ThresholdValue }
                }
            }));
        await _context.SaveChangesAsync(cancellationToken);

        return new UpdateStandardParamResult(true, "Param updated.");
    }
}

public record DeleteStandardParamResult(bool Success, string Message, string? ErrorCode = null);

public record DeleteStandardParamCommand(Guid TemplateId, Guid VersionId, Guid ItemId, Guid ParamId, Guid CurrentUserId)
    : IRequest<DeleteStandardParamResult>;

public class DeleteStandardParamCommandHandler : IRequestHandler<DeleteStandardParamCommand, DeleteStandardParamResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public DeleteStandardParamCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<DeleteStandardParamResult> Handle(DeleteStandardParamCommand request, CancellationToken cancellationToken)
    {
        var t = await MaintenanceTemplateAccess.GetVisibleTemplateAsync(_context, _companyScope, request.TemplateId);
        if (t == null)
            return new DeleteStandardParamResult(false, "Not found.", "NOT_FOUND");
        var version = await MaintenanceTemplateAccess.GetVersionOfTemplateAsync(_context, t, request.VersionId);
        if (version == null)
            return new DeleteStandardParamResult(false, "Version not found.", "VERSION_NOT_FOUND");
        var param = await _context.MaintenanceStandardParams
            .FirstOrDefaultAsync(p => p.Id == request.ParamId && p.ChecklistItemId == request.ItemId, cancellationToken);
        if (param == null)
            return new DeleteStandardParamResult(false, "Param not found.", "PARAM_NOT_FOUND");
        if (await MaintenanceTemplateAccess.VersionHasCampaignsAsync(_context, request.VersionId))
            return new DeleteStandardParamResult(false, MaintenanceTemplateItems.FrozenMessage(version), "TEMPLATE_VERSION_IN_USE");

        var name = param.ParamName;
        _context.MaintenanceStandardParams.Remove(param);
        await _context.SaveChangesAsync(cancellationToken);

        _actionLogService.Log(MaintenanceTemplateLogging.Build(
            ActionType.Delete, t, request.CurrentUserId,
            $"Xóa tiêu chuẩn \"{name}\" khỏi hạng mục (version {version.VersionNumber})",
            new { scope = "param", versionId = request.VersionId, versionNumber = version.VersionNumber, itemId = request.ItemId, paramId = request.ParamId }));
        await _context.SaveChangesAsync(cancellationToken);
        return new DeleteStandardParamResult(true, "Param deleted.");
    }
}
