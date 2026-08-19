namespace aspire_react.Server.Application.Accessories.Commands;

public record AccessoryResult(bool Success, string Message, Guid? AccessoryId = null, string? ErrorCode = null);