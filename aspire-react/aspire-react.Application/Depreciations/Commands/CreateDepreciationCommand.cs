using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Depreciations.Commands;

public record DepreciationResult(bool Success, Guid? Id, string? ErrorCode, string Message);

public record CreateDepreciationCommand(string Name, int Months, Guid CurrentUserId)
    : IRequest<DepreciationResult>, ILoggableCommand<DepreciationResult>
{
    public ActionLogEntry? BuildLogEntry(DepreciationResult response)
    {
        if (!response.Success || response.Id is null) return null;
        return new ActionLogEntry
        {
            ItemType = ItemType.Depreciation,
            ItemId = response.Id.Value,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            Note = $"Tạo cấu hình khấu hao \"{Name}\" ({Months} tháng)",
        };
    }
}

public class CreateDepreciationCommandHandler : IRequestHandler<CreateDepreciationCommand, DepreciationResult>
{
    private readonly IApplicationDbContext _context;

    public CreateDepreciationCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<DepreciationResult> Handle(CreateDepreciationCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (name.Length == 0) return new(false, null, "VALIDATION_ERROR", "Tên cấu hình khấu hao không được để trống.");
        if (request.Months is < 1 or > 600)
            return new(false, null, "VALIDATION_ERROR", "Số tháng khấu hao phải từ 1 đến 600.");

        var duplicate = await _context.Depreciations
            .AnyAsync(d => d.Name.ToLower() == name.ToLower(), cancellationToken);
        if (duplicate) return new(false, null, "DUPLICATE_NAME", "Tên cấu hình khấu hao đã tồn tại.");

        var entity = new Depreciation { Name = name, Months = request.Months };
        _context.Depreciations.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return new(true, entity.Id, null, "Đã tạo cấu hình khấu hao.");
    }
}
