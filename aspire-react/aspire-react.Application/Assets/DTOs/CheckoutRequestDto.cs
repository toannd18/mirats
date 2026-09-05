namespace aspire_react.Server.Application.Assets.DTOs;

public record CheckoutRequestDto(
    Domain.Enums.AssignmentTargetType TargetType,
    Guid TargetId,
    string? Note,
    DateTime? CheckoutAt,
    Guid? StatusId);

public record CheckinRequestDto(
    string? Note,
    DateTime? CheckinAt);

public record AuditRequestDto(
    DateTime? AuditDate,
    string? Note);