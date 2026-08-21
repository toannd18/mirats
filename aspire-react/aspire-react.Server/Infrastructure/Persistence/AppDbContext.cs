using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Infrastructure.Persistence;

public class AppDbContext : DbContext, IApplicationDbContext
{
    private readonly ICompanyScopeService? _companyScope;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICompanyScopeService? companyScope = null) : base(options)
    {
        _companyScope = companyScope;
    }

    // Auth & Permission
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<User> Users => Set<User>();
    public DbSet<PermissionGroup> PermissionGroups => Set<PermissionGroup>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<GroupPermission> GroupPermissions => Set<GroupPermission>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();

    // Asset Management
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetModel> Models => Set<AssetModel>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Manufacturer> Manufacturers => Set<Manufacturer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<SystemInfo> SystemInfos => Set<SystemInfo>();
    public DbSet<SystemPosition> SystemPositions => Set<SystemPosition>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<StatusLabel> StatusLabels => Set<StatusLabel>();
    public DbSet<Depreciation> Depreciations => Set<Depreciation>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<ActionLog> ActionLogs => Set<ActionLog>();

    // Consumables, Components, Accessories, Licenses
    public DbSet<Consumable> Consumables => Set<Consumable>();
    public DbSet<ConsumableCheckout> ConsumableCheckouts => Set<ConsumableCheckout>();
    public DbSet<Component> Components => Set<Component>();
    public DbSet<ComponentUnit> ComponentUnits => Set<ComponentUnit>();
    public DbSet<ComponentAssignment> ComponentAssignments => Set<ComponentAssignment>();
    public DbSet<Accessory> Accessories => Set<Accessory>();
    public DbSet<AccessoryCheckout> AccessoryCheckouts => Set<AccessoryCheckout>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<LicenseSeat> LicenseSeats => Set<LicenseSeat>();
    public DbSet<AssetMaintenance> AssetMaintenances => Set<AssetMaintenance>();
    public DbSet<AssetMaintenanceAssignee> AssetMaintenanceAssignees => Set<AssetMaintenanceAssignee>();

    // Custom Fields
    public DbSet<CustomField> CustomFields => Set<CustomField>();
    public DbSet<CustomFieldset> CustomFieldsets => Set<CustomFieldset>();
    public DbSet<CustomFieldFieldset> CustomFieldFieldsets => Set<CustomFieldFieldset>();

    // System configuration & auto-gen counters (Task ASSET-TAG-AUTO)
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<AssetTagCounter> AssetTagCounters => Set<AssetTagCounter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // === Auth & Permission ===
        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("companies");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(20);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasOne(e => e.Parent).WithMany(e => e.Children).HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.HasOne(e => e.Company).WithMany(e => e.Users).HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Department).WithMany().HasForeignKey(e => e.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Location).WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PermissionGroup>(entity =>
        {
            entity.ToTable("permission_groups");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.IsSystem).HasDefaultValue(false);
        });

        modelBuilder.Entity<UserPermission>(entity =>
        {
            entity.ToTable("user_permissions");
            entity.HasKey(e => new { e.UserId, e.PermissionKey });
            entity.Property(e => e.PermissionKey).IsRequired().HasMaxLength(100);
            entity.HasOne(e => e.User).WithMany(e => e.UserPermissions).HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GroupPermission>(entity =>
        {
            entity.ToTable("group_permissions");
            entity.HasKey(e => new { e.GroupId, e.PermissionKey });
            entity.Property(e => e.PermissionKey).IsRequired().HasMaxLength(100);
            entity.HasOne(e => e.Group).WithMany(e => e.GroupPermissions).HasForeignKey(e => e.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserGroup>(entity =>
        {
            entity.ToTable("user_groups");
            entity.HasKey(e => new { e.UserId, e.GroupId });
            entity.HasOne(e => e.User).WithMany(e => e.UserGroups).HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Group).WithMany(e => e.UserGroups).HasForeignKey(e => e.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        // === Asset Management ===
        modelBuilder.Entity<Asset>(entity =>
        {
            entity.ToTable("assets");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.AssetTag).IsRequired().HasMaxLength(255);
            entity.HasIndex(e => e.AssetTag).IsUnique();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.HasIndex(e => e.Serial);
            entity.Property(e => e.Status).HasConversion<int>().HasDefaultValueSql("0");
            entity.Property(e => e.IsConfirmed).HasDefaultValueSql("false");
            entity.HasOne(e => e.Model).WithMany(e => e.Assets).HasForeignKey(e => e.ModelId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Location).WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Supplier).WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.SetNull);
            // Current assignment pointer: Asset.CurrentAssignment → Assignment (one-to-one),
            // FK on assets.CurrentAssignmentId. When null the asset is available.
            entity.HasOne(e => e.CurrentAssignment).WithOne().HasForeignKey<Asset>(e => e.CurrentAssignmentId).OnDelete(DeleteBehavior.SetNull);
            // Assignment history: Assignment.Asset ↔ Asset.ChildAssignments (one-to-many),
            // FK on assignments.AssetId — every checkout/assignment row is kept as history.
            entity.HasMany(e => e.ChildAssignments).WithOne(a => a.Asset).HasForeignKey(a => a.AssetId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AssetModel>(entity =>
        {
            entity.ToTable("models");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.HasOne(e => e.Manufacturer).WithMany().HasForeignKey(e => e.ManufacturerId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Category).WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Depreciation).WithMany().HasForeignKey(e => e.DepreciationId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("categories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
        });

        modelBuilder.Entity<Manufacturer>(entity =>
        {
            entity.ToTable("manufacturers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Code).HasColumnType("text").IsRequired(false);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.ToTable("suppliers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Code).HasColumnType("text").IsRequired(false);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("departments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasColumnType("text");
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Manager).WithMany().HasForeignKey(e => e.ManagerId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SystemInfo>(entity =>
        {
            entity.ToTable("system_infos");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Code).IsRequired().HasColumnType("text");
            entity.Property(e => e.Name).IsRequired().HasColumnType("text");
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SystemPosition>(entity =>
        {
            entity.ToTable("system_positions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Code).IsRequired().HasColumnType("text");
            entity.Property(e => e.Name).IsRequired().HasColumnType("text");
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasOne(e => e.SystemInfo).WithMany(s => s.Positions).HasForeignKey(e => e.SystemInfoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.ToTable("locations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.HasOne(e => e.Parent).WithMany(e => e.Children).HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Manager).WithMany().HasForeignKey(e => e.ManagerId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<StatusLabel>(entity =>
        {
            entity.ToTable("status_labels");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<Depreciation>(entity =>
        {
            entity.ToTable("depreciations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
        });

        modelBuilder.Entity<Assignment>(entity =>
        {
            entity.ToTable("assignments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => new { e.TargetType, e.TargetId });
            entity.HasIndex(e => e.TargetId);
            entity.Ignore(e => e.AssignedUser);
            entity.HasOne(e => e.AssignedBy).WithMany().HasForeignKey(e => e.AssignedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ActionLog>(entity =>
        {
            entity.ToTable("action_logs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => new { e.ItemType, e.ItemId });
            entity.HasIndex(e => e.TargetSystemInfoId);
            entity.Property(e => e.DeletedAt).HasColumnType("timestamp without time zone");
            entity.HasOne(e => e.Creator).WithMany().HasForeignKey(e => e.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        });

        // === Consumables, Components, Accessories, Licenses ===
        modelBuilder.Entity<Consumable>(entity =>
        {
            entity.ToTable("consumables");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.HasIndex(e => e.ItemNo);
            entity.Property(e => e.Status).HasDefaultValueSql("1");
            entity.HasOne(e => e.Category).WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Manufacturer).WithMany().HasForeignKey(e => e.ManufacturerId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Supplier).WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Location).WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ConsumableCheckout>(entity =>
        {
            entity.ToTable("consumable_checkouts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasOne(e => e.Consumable).WithMany(c => c.Checkouts).HasForeignKey(e => e.ConsumableId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Component>(entity =>
        {
            entity.ToTable("components");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.HasIndex(e => e.Serial);
            entity.Property(e => e.TrackingType).HasConversion<int>().HasDefaultValueSql("0");
            entity.HasIndex(e => e.CategoryId);
            entity.HasIndex(e => e.CompanyId);
            entity.HasIndex(e => e.LocationId);
            entity.HasIndex(e => e.ManufacturerId);
            entity.HasIndex(e => e.SupplierId);
            entity.HasOne(e => e.Category).WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
            // Company/Location: RESTRICT — a company/location referenced by a component
            // (incl. soft-deleted) cannot be deleted; API guards return COMPANY_IN_USE / LOCATION_IN_USE.
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Location).WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Manufacturer).WithMany().HasForeignKey(e => e.ManufacturerId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Supplier).WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ComponentUnit>(entity =>
        {
            entity.ToTable("component_units");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.SerialNo).HasColumnType("text");
            // Unique serial — Postgres allows multiple NULLs, so a NULL SerialNo is fine.
            entity.HasIndex(e => e.SerialNo).IsUnique();
            entity.HasIndex(e => e.ComponentId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CurrentAssetId);
            entity.Property(e => e.Status).HasConversion<int>().HasDefaultValueSql("0");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone");
            entity.Property(e => e.DeletedAt).HasColumnType("timestamp without time zone");
            entity.HasOne(e => e.Component).WithMany(c => c.Units).HasForeignKey(e => e.ComponentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.CurrentAsset).WithMany().HasForeignKey(e => e.CurrentAssetId).OnDelete(DeleteBehavior.SetNull);
            // Soft delete — units are never hard-deleted so the per-serial audit trail survives.
            entity.HasQueryFilter(e => e.DeletedAt == null);
        });

        modelBuilder.Entity<ComponentAssignment>(entity =>
        {
            entity.ToTable("component_assignments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasOne(e => e.Component).WithMany(c => c.Assignments).HasForeignKey(e => e.ComponentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Asset).WithMany().HasForeignKey(e => e.AssetId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Accessory>(entity =>
        {
            entity.ToTable("accessories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.HasIndex(e => e.ItemNo);
            entity.HasOne(e => e.Category).WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Manufacturer).WithMany().HasForeignKey(e => e.ManufacturerId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Supplier).WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Location).WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AccessoryCheckout>(entity =>
        {
            entity.ToTable("accessory_checkouts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CheckoutType).HasConversion<int>().HasDefaultValueSql("1");
            entity.Property(e => e.TargetId).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.AssignedQty).HasDefaultValueSql("1");
            entity.Property(e => e.ReturnedQty).HasDefaultValueSql("0");
            entity.HasOne(e => e.Accessory).WithMany(a => a.Checkouts).HasForeignKey(e => e.AccessoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<License>(entity =>
        {
            entity.ToTable("licenses");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Serial).HasMaxLength(255);
            entity.Property(e => e.Reassignable).HasDefaultValueSql("true");
            entity.Property(e => e.DeletedAt).HasColumnType("timestamp without time zone");
            entity.Property(e => e.TerminationDate).HasColumnType("timestamp without time zone");
            entity.HasOne(e => e.Manufacturer).WithMany().HasForeignKey(e => e.ManufacturerId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Category).WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<LicenseSeat>(entity =>
        {
            entity.ToTable("license_seats");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasOne(e => e.License).WithMany(l => l.LicenseSeats).HasForeignKey(e => e.LicenseId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Asset).WithMany().HasForeignKey(e => e.AssetId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AssetMaintenance>(entity =>
        {
            entity.ToTable("asset_maintenances");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Title).IsRequired().HasColumnType("text");
            entity.Property(e => e.Type).HasConversion<int>();
            entity.Property(e => e.CompanyId).HasDefaultValueSql("'00000000-0000-0000-0000-000000000000'::uuid");
            entity.Property(e => e.StartDate).HasColumnType("timestamp without time zone");
            entity.Property(e => e.CompletionDate).HasColumnType("timestamp without time zone");
            entity.Property(e => e.ClosedAt).HasColumnType("timestamp without time zone");
            entity.Property(e => e.InspectedAt).HasColumnType("timestamp without time zone");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone");
            entity.Property(e => e.DeletedAt).HasColumnType("timestamp without time zone");
            entity.Property(e => e.IsWarranty).HasDefaultValueSql("false");
            entity.Property(e => e.IsClosed).HasDefaultValueSql("false");
            entity.HasIndex(e => e.AssetId);
            entity.HasIndex(e => e.CompanyId);
            entity.HasIndex(e => e.SnapshotSystemInfoId);
            entity.HasIndex(e => e.SnapshotSystemPositionId);
            entity.HasOne(e => e.Asset).WithMany().HasForeignKey(e => e.AssetId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Supplier).WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.InspectedBy).WithMany().HasForeignKey(e => e.InspectedById).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AssetMaintenanceAssignee>(entity =>
        {
            entity.ToTable("asset_maintenance_assignees");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => new { e.MaintenanceId, e.UserId }).IsUnique();
            entity.HasIndex(e => e.MaintenanceId);
            entity.Property(e => e.AssignedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(e => e.Maintenance).WithMany(m => m.Assignees).HasForeignKey(e => e.MaintenanceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<License>(entity =>
        {
            entity.ToTable("licenses");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => e.CompanyId);
            entity.HasIndex(e => e.CategoryId);
            entity.HasIndex(e => e.Name);
            entity.HasOne(e => e.Supplier).WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Manufacturer).WithMany().HasForeignKey(e => e.ManufacturerId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Category).WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(e => e.LicenseSeats).WithOne(s => s.License).HasForeignKey(s => s.LicenseId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LicenseSeat>(entity =>
        {
            entity.ToTable("license_seats");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => e.LicenseId);
            entity.HasIndex(e => new { e.LicenseId, e.SeatNumber }).IsUnique();
            entity.Property(e => e.SeatNumber).HasDefaultValueSql("0");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasCheckConstraint("CK_license_seats_single_target", "(((\"UserId\" IS NOT NULL)::int + (\"AssetId\" IS NOT NULL)::int + (\"SystemInfoId\" IS NOT NULL)::int) <= 1)");
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Asset).WithMany().HasForeignKey(e => e.AssetId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.SystemInfo).WithMany().HasForeignKey(e => e.SystemInfoId).OnDelete(DeleteBehavior.SetNull);
        });

        // === Custom Fields ===
        modelBuilder.Entity<CustomField>(entity =>
        {
            entity.ToTable("custom_fields");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.HasIndex(e => e.Slug).IsUnique();
        });

        modelBuilder.Entity<CustomFieldset>(entity =>
        {
            entity.ToTable("custom_fieldsets");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
        });

        modelBuilder.Entity<CustomFieldFieldset>(entity =>
        {
            entity.ToTable("custom_field_fieldsets");
            entity.HasKey(e => new { e.FieldsetId, e.FieldId });
            entity.HasOne(e => e.Fieldset).WithMany(fs => fs.CustomFieldFieldsets).HasForeignKey(e => e.FieldsetId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Field).WithMany(cf => cf.CustomFieldFieldsets).HasForeignKey(e => e.FieldId).OnDelete(DeleteBehavior.Cascade);
        });

        // === System configuration & auto-gen counters (Task ASSET-TAG-AUTO) ===
        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.ToTable("system_settings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Key).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Key).IsUnique();
            entity.Property(e => e.Value).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<AssetTagCounter>(entity =>
        {
            entity.ToTable("asset_tag_counters");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => new { e.CompanyId, e.Year }).IsUnique();
        });

        // === Global Query Filters (FMCS Multi-tenant) ===
        ConfigureCompanyFilter<Asset>(modelBuilder);
        ConfigureCompanyFilter<Consumable>(modelBuilder);
        ConfigureCompanyFilter<Component>(modelBuilder);
        ConfigureCompanyFilter<Accessory>(modelBuilder);
        ConfigureCompanyFilter<License>(modelBuilder);
    }

    private void ConfigureCompanyFilter<T>(ModelBuilder modelBuilder) where T : class, ICompanyable
    {
        modelBuilder.Entity<T>().HasQueryFilter(entity =>
            _companyScope == null ||
            _companyScope.IsSuperUser() ||
            entity.CompanyId == null ||
            _companyScope.GetUserCompanyIdsAsync().Result.Count == 0 ||
            _companyScope.GetUserCompanyIdsAsync().Result.Contains(entity.CompanyId.Value));
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // ComponentUnit's audit columns are `timestamp without time zone` (ST6d) — Npgsql
                    // rejects Kind=UTC there, so write Unspecified (same convention as LicenseSeat).
                    // All other IAuditable entities use `timestamp with time zone` and keep Kind=UTC.
                    if (entry.Entity is ComponentUnit)
                    {
                        entry.Entity.CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                        entry.Entity.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                    }
                    else
                    {
                        entry.Entity.CreatedAt = DateTime.UtcNow;
                        entry.Entity.UpdatedAt = DateTime.UtcNow;
                    }
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = entry.Entity is ComponentUnit
                        ? DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                        : DateTime.UtcNow;
                    break;
            }
        }
        return await base.SaveChangesAsync(cancellationToken);
    }
}