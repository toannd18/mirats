using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.MaintenanceTemplates.Commands;

// ─── Shared result shape for version lifecycle ops (controller returned the same 4-field object) ───

public record MaintenanceTemplateVersionView(
    Guid Id, int VersionNumber, DateTime? EffectiveFrom, DateTime? PublishedAt, bool IsCurrent);

// ─── CREATE VERSION (draft, auto-numbered) ───

public record CreateTemplateVersionResult(
    bool Success, string Message, string? ErrorCode = null, MaintenanceTemplateVersionView? Version = null);

public record CreateTemplateVersionCommand(Guid TemplateId, DateTime? EffectiveFrom, Guid CurrentUserId)
    : IRequest<CreateTemplateVersionResult>;

public class CreateTemplateVersionCommandHandler : IRequestHandler<CreateTemplateVersionCommand, CreateTemplateVersionResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public CreateTemplateVersionCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<CreateTemplateVersionResult> Handle(CreateTemplateVersionCommand request, CancellationToken cancellationToken)
    {
        var t = await MaintenanceTemplateAccess.GetVisibleTemplateAsync(_context, _companyScope, request.TemplateId);
        if (t == null)
            return new CreateTemplateVersionResult(false, "Not found.", "NOT_FOUND");

        var nextNumber = (await _context.MaintenanceChecklistTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == request.TemplateId)
            .MaxAsync(v => (int?)v.VersionNumber, cancellationToken) ?? 0) + 1;

        var version = new MaintenanceChecklistTemplateVersion
        {
            TemplateId = request.TemplateId,
            VersionNumber = nextNumber,
            EffectiveFrom = request.EffectiveFrom.HasValue
                ? MaintenanceTemplateAccess.ToUtc(request.EffectiveFrom.Value)
                : null,
            CreatedById = request.CurrentUserId
        };
        _context.MaintenanceChecklistTemplateVersions.Add(version);
        await _context.SaveChangesAsync(cancellationToken);

        _actionLogService.Log(MaintenanceTemplateLogging.Build(
            ActionType.Create, t, request.CurrentUserId,
            $"Tạo bản nháp version {nextNumber} cho template \"{t.Name}\"",
            new { scope = "version", versionId = version.Id, versionNumber = nextNumber },
            itemId: version.Id));
        await _context.SaveChangesAsync(cancellationToken);

        return new CreateTemplateVersionResult(true, "Created.", Version: new MaintenanceTemplateVersionView(
            version.Id, version.VersionNumber, version.EffectiveFrom, version.PublishedAt, version.IsCurrent));
    }
}

// ─── PUBLISH (draft → current; ordered saves inside ONE explicit transaction) ───

public record PublishTemplateVersionResult(
    bool Success, string Message, string? ErrorCode = null, MaintenanceTemplateVersionView? Version = null);

public record PublishTemplateVersionCommand(Guid TemplateId, Guid VersionId, Guid CurrentUserId)
    : IRequest<PublishTemplateVersionResult>;

public class PublishTemplateVersionCommandHandler : IRequestHandler<PublishTemplateVersionCommand, PublishTemplateVersionResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public PublishTemplateVersionCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<PublishTemplateVersionResult> Handle(PublishTemplateVersionCommand request, CancellationToken cancellationToken)
    {
        var t = await MaintenanceTemplateAccess.GetVisibleTemplateAsync(_context, _companyScope, request.TemplateId);
        if (t == null)
            return new PublishTemplateVersionResult(false, "Not found.", "NOT_FOUND");
        var version = await MaintenanceTemplateAccess.GetVersionOfTemplateAsync(_context, t, request.VersionId);
        if (version == null)
            return new PublishTemplateVersionResult(false, "Version not found.", "VERSION_NOT_FOUND");

        if (version.PublishedAt.HasValue)
            return new PublishTemplateVersionResult(false,
                $"Version {version.VersionNumber} đã được publish trước đó.", "VERSION_ALREADY_PUBLISHED");

        // ── Demote FIRST, promote SECOND — inside ONE explicit transaction. ──
        // Postgres enforces the filtered unique index ("IsCurrent" = true) PER STATEMENT: promoting
        // the new current before demoting the old one momentarily leaves TWO current rows and fails
        // with a raw 23505 → HTTP 500 (reproduced live; InMemory tests cannot catch this because
        // they don't enforce unique indexes). Ordered saves keep every intermediate state valid
        // while the transaction keeps the flip atomic.
        //
        // ⚠️ Aspire's AddNpgsqlDbContext registers a RETRYING execution strategy — a user-initiated
        // transaction is ONLY legal inside CreateExecutionStrategy().ExecuteAsync (same convention
        // as ComponentsController/Checkout handlers). A bare BeginTransactionAsync throws
        // InvalidOperationException → HTTP 500 before touching a single row.
        // Transaction boundary preserved verbatim — this is why Publish does NOT implement
        // ILoggableCommand (nested BeginTransaction would throw); the log stays AFTER the commit,
        // exactly like the pre-migration controller.
        var strategy = _context.Database.CreateExecutionStrategy();
        Guid[] demotedIds = Array.Empty<Guid>();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

            var others = await _context.MaintenanceChecklistTemplateVersions
                .Where(v => v.TemplateId == request.TemplateId && v.IsCurrent && v.Id != version.Id)
                .ToListAsync(cancellationToken);
            foreach (var other in others) other.IsCurrent = false;
            demotedIds = others.Select(o => o.Id).ToArray();
            await _context.SaveChangesAsync(cancellationToken);

            version.PublishedAt = DateTime.UtcNow;
            if (!version.EffectiveFrom.HasValue) version.EffectiveFrom = version.PublishedAt;
            version.IsCurrent = true;
            await _context.SaveChangesAsync(cancellationToken);

            await tx.CommitAsync(cancellationToken);
        });

        _actionLogService.Log(MaintenanceTemplateLogging.Build(
            ActionType.Publish, t, request.CurrentUserId,
            $"Publish version {version.VersionNumber} cho template \"{t.Name}\"",
            new { scope = "version", versionId = version.Id, versionNumber = version.VersionNumber, demotedVersionIds = demotedIds },
            itemId: version.Id));
        await _context.SaveChangesAsync(cancellationToken);

        return new PublishTemplateVersionResult(true, "Published.", Version: new MaintenanceTemplateVersionView(
            version.Id, version.VersionNumber, version.EffectiveFrom, version.PublishedAt, version.IsCurrent));
    }
}

// ─── UPDATE VERSION (metadata — currently EffectiveFrom only) ───

public record UpdateTemplateVersionResult(
    bool Success, string Message, string? ErrorCode = null, MaintenanceTemplateVersionView? Version = null);

public record UpdateTemplateVersionCommand(Guid TemplateId, Guid VersionId, DateTime? EffectiveFrom, Guid CurrentUserId)
    : IRequest<UpdateTemplateVersionResult>;

public class UpdateTemplateVersionCommandHandler : IRequestHandler<UpdateTemplateVersionCommand, UpdateTemplateVersionResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public UpdateTemplateVersionCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<UpdateTemplateVersionResult> Handle(UpdateTemplateVersionCommand request, CancellationToken cancellationToken)
    {
        var t = await MaintenanceTemplateAccess.GetVisibleTemplateAsync(_context, _companyScope, request.TemplateId);
        if (t == null)
            return new UpdateTemplateVersionResult(false, "Not found.", "NOT_FOUND");
        var version = await MaintenanceTemplateAccess.GetVersionOfTemplateAsync(_context, t, request.VersionId);
        if (version == null)
            return new UpdateTemplateVersionResult(false, "Version not found.", "VERSION_NOT_FOUND");

        // ── IMMUTABLE GUARD (MC-2 core): campaigns pinning this version freeze it entirely. ──
        if (await MaintenanceTemplateAccess.VersionHasCampaignsAsync(_context, request.VersionId))
            return new UpdateTemplateVersionResult(false,
                $"Version {version.VersionNumber} đã có đợt bảo dưỡng tham chiếu — không thể sửa. Hãy tạo version mới.",
                "TEMPLATE_VERSION_IN_USE");

        var before = version.EffectiveFrom;
        if (request.EffectiveFrom.HasValue)
        {
            version.EffectiveFrom = MaintenanceTemplateAccess.ToUtc(request.EffectiveFrom.Value);
            await _context.SaveChangesAsync(cancellationToken);
            _actionLogService.Log(MaintenanceTemplateLogging.Build(
                ActionType.Update, t, request.CurrentUserId,
                $"Cập nhật version {version.VersionNumber} của template \"{t.Name}\"",
                new { scope = "version", versionId = version.Id, changes = new { effectiveFrom = new { old = before, @new = version.EffectiveFrom } } },
                itemId: version.Id));
            await _context.SaveChangesAsync(cancellationToken);
        }
        return new UpdateTemplateVersionResult(true, "Updated.", Version: new MaintenanceTemplateVersionView(
            version.Id, version.VersionNumber, version.EffectiveFrom, version.PublishedAt, version.IsCurrent));
    }
}

// ─── DELETE VERSION (draft only) ───

public record DeleteTemplateVersionResult(bool Success, string Message, string? ErrorCode = null);

public record DeleteTemplateVersionCommand(Guid TemplateId, Guid VersionId, Guid CurrentUserId)
    : IRequest<DeleteTemplateVersionResult>;

public class DeleteTemplateVersionCommandHandler : IRequestHandler<DeleteTemplateVersionCommand, DeleteTemplateVersionResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public DeleteTemplateVersionCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<DeleteTemplateVersionResult> Handle(DeleteTemplateVersionCommand request, CancellationToken cancellationToken)
    {
        var t = await MaintenanceTemplateAccess.GetVisibleTemplateAsync(_context, _companyScope, request.TemplateId);
        if (t == null)
            return new DeleteTemplateVersionResult(false, "Not found.", "NOT_FOUND");
        var version = await MaintenanceTemplateAccess.GetVersionOfTemplateAsync(_context, t, request.VersionId);
        if (version == null)
            return new DeleteTemplateVersionResult(false, "Version not found.", "VERSION_NOT_FOUND");

        // Guard order matters: IN_USE (campaign) beats ALREADY_PUBLISHED — the more specific problem first.
        if (await MaintenanceTemplateAccess.VersionHasCampaignsAsync(_context, request.VersionId))
            return new DeleteTemplateVersionResult(false,
                $"Version {version.VersionNumber} đã có đợt bảo dưỡng tham chiếu — không thể xóa.",
                "TEMPLATE_VERSION_IN_USE");
        if (version.PublishedAt.HasValue)
            return new DeleteTemplateVersionResult(false,
                $"Version {version.VersionNumber} đã publish — không thể xóa (chỉ version nháp được xóa).",
                "VERSION_ALREADY_PUBLISHED");

        var number = version.VersionNumber;
        _context.MaintenanceChecklistTemplateVersions.Remove(version); // items/params cascade
        await _context.SaveChangesAsync(cancellationToken);

        _actionLogService.Log(MaintenanceTemplateLogging.Build(
            ActionType.Delete, t, request.CurrentUserId,
            $"Xóa bản nháp version {number} của template \"{t.Name}\"",
            new { scope = "version", versionId = request.VersionId, versionNumber = number },
            itemId: version.Id));
        await _context.SaveChangesAsync(cancellationToken);
        return new DeleteTemplateVersionResult(true, "Version deleted.");
    }
}
