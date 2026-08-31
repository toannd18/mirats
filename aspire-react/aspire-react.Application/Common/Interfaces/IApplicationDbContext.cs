using aspire_react.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace aspire_react.Server.Application.Common.Interfaces;

/// <summary>
/// [Giai đoạn 0.1 — F2 phương án A, pattern Jason Taylor] Application-layer EF Core abstraction:
/// handlers/validators inject THIS instead of Infrastructure's AppDbContext, so the Application
/// project never references Infrastructure (compiler-enforced dependency direction).
/// Exposes the SAME surface the handlers already use (every AppDbContext DbSet + Database facade +
/// SaveChangesAsync) — zero behavior change. Implemented by AppDbContext (Infrastructure); bound in
/// DI to the same scoped instance (see PersistenceServiceCollectionExtensions.AddPersistence).
/// </summary>
public interface IApplicationDbContext
{
    /// <summary>Relational facade — CreateExecutionStrategy / BeginTransactionAsync / ProviderName (Task O-FIX concurrency pattern).</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // Auth & Permission
    DbSet<Company> Companies { get; }
    DbSet<User> Users { get; }
    DbSet<PermissionGroup> PermissionGroups { get; }
    DbSet<UserPermission> UserPermissions { get; }
    DbSet<GroupPermission> GroupPermissions { get; }
    DbSet<UserGroup> UserGroups { get; }

    // Asset Management
    DbSet<Asset> Assets { get; }
    DbSet<AssetModel> Models { get; }
    DbSet<Category> Categories { get; }
    DbSet<Manufacturer> Manufacturers { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<Department> Departments { get; }
    DbSet<SystemInfo> SystemInfos { get; }
    DbSet<SystemPosition> SystemPositions { get; }
    DbSet<Location> Locations { get; }
    DbSet<StatusLabel> StatusLabels { get; }
    DbSet<Depreciation> Depreciations { get; }
    DbSet<Assignment> Assignments { get; }
    DbSet<ActionLog> ActionLogs { get; }

    // Consumables, Components, Accessories, Licenses
    DbSet<Consumable> Consumables { get; }
    DbSet<ConsumableCheckout> ConsumableCheckouts { get; }
    DbSet<Component> Components { get; }
    DbSet<ComponentUnit> ComponentUnits { get; }
    DbSet<ComponentAssignment> ComponentAssignments { get; }
    DbSet<Accessory> Accessories { get; }
    DbSet<AccessoryCheckout> AccessoryCheckouts { get; }
    DbSet<License> Licenses { get; }
    DbSet<LicenseSeat> LicenseSeats { get; }
    DbSet<AssetMaintenance> AssetMaintenances { get; }
    DbSet<AssetMaintenanceAssignee> AssetMaintenanceAssignees { get; }

    // Custom Fields
    DbSet<CustomField> CustomFields { get; }
    DbSet<CustomFieldset> CustomFieldsets { get; }
    DbSet<CustomFieldFieldset> CustomFieldFieldsets { get; }

    // System configuration & auto-gen counters (Task ASSET-TAG-AUTO)
    DbSet<SystemSetting> SystemSettings { get; }
    DbSet<AssetTagCounter> AssetTagCounters { get; }

    // Maintenance checklist (MC — đa template có version, campaign cấp Hệ thống)
    DbSet<MaintenanceChecklistTemplate> MaintenanceChecklistTemplates { get; }
    DbSet<MaintenanceChecklistTemplateVersion> MaintenanceChecklistTemplateVersions { get; }
    DbSet<MaintenanceChecklistItem> MaintenanceChecklistItems { get; }
    DbSet<MaintenanceChecklistItemPosition> MaintenanceChecklistItemPositions { get; }
    DbSet<MaintenanceStandardParam> MaintenanceStandardParams { get; }
    DbSet<MaintenanceCampaign> MaintenanceCampaigns { get; }
    DbSet<MaintenanceCampaignExecutor> MaintenanceCampaignExecutors { get; }
    DbSet<MaintenanceCampaignDeviceSnapshot> MaintenanceCampaignDeviceSnapshots { get; }
    DbSet<MaintenanceChecklistResult> MaintenanceChecklistResults { get; }
}
