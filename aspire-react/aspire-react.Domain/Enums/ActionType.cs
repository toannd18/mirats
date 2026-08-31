namespace aspire_react.Server.Domain.Enums;

public enum ActionType
{
    Create = 1,
    Update = 2,
    Delete = 3,
    Checkout = 4,
    Checkin = 5,
    Audit = 6,
    Import = 7,
    Export = 8,
    Accept = 9,
    Decline = 10,
    Confirm = 11,
    Archive = 12,
    Unarchive = 13,
    UpdateRejected = 14,
    StockIn = 15,
    MarkDamaged = 16,
    Dispose = 17,
    Close = 18,
    Reopen = 19,
    Inspect = 20,
    /// <summary>[MC-2] Publish một MaintenanceChecklistTemplateVersion (draft → hiện hành).</summary>
    Publish = 21,
    /// <summary>[MC-3] Hoàn thành một MaintenanceCampaign (status → Completed, tính NextMaintenanceDueDate).</summary>
    Complete = 22
}