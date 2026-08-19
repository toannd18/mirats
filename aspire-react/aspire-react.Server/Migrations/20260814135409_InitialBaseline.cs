using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace aspirereact.Server.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CategoryType = table.Column<int>(type: "integer", nullable: false),
                    TagColor = table.Column<string>(type: "text", nullable: true),
                    UseDefaultEula = table.Column<bool>(type: "boolean", nullable: false),
                    RequireAcceptance = table.Column<bool>(type: "boolean", nullable: false),
                    CheckinEmail = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_companies_companies_ParentId",
                        column: x => x.ParentId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "custom_fields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Format = table.Column<string>(type: "text", nullable: false),
                    Element = table.Column<string>(type: "text", nullable: true),
                    FieldValues = table.Column<string>(type: "text", nullable: true),
                    FieldEncrypted = table.Column<bool>(type: "boolean", nullable: false),
                    HelpText = table.Column<string>(type: "text", nullable: true),
                    ShowInEmail = table.Column<bool>(type: "boolean", nullable: false),
                    IsUnique = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_fields", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "custom_fieldsets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_fieldsets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "depreciations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Months = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_depreciations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "manufacturers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Code = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Url = table.Column<string>(type: "text", nullable: true),
                    SupportUrl = table.Column<string>(type: "text", nullable: true),
                    SupportEmail = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manufacturers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "permission_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permission_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "status_labels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Deployable = table.Column<bool>(type: "boolean", nullable: false),
                    Pending = table.Column<bool>(type: "boolean", nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    StatusType = table.Column<string>(type: "text", nullable: true),
                    Color = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_status_labels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Code = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Url = table.Column<string>(type: "text", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    Zip = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Fax = table.Column<string>(type: "text", nullable: true),
                    ContactName = table.Column<string>(type: "text", nullable: true),
                    ContactEmail = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "system_infos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_infos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_system_infos_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "custom_field_fieldsets",
                columns: table => new
                {
                    FieldsetId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    Required = table.Column<bool>(type: "boolean", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_field_fieldsets", x => new { x.FieldsetId, x.FieldId });
                    table.ForeignKey(
                        name: "FK_custom_field_fieldsets_custom_fields_FieldId",
                        column: x => x.FieldId,
                        principalTable: "custom_fields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_custom_field_fieldsets_custom_fieldsets_FieldsetId",
                        column: x => x.FieldsetId,
                        principalTable: "custom_fieldsets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "models",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ModelNumber = table.Column<string>(type: "text", nullable: true),
                    ManufacturerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    DepreciationId = table.Column<Guid>(type: "uuid", nullable: true),
                    FieldsetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Eol = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Requestable = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_models", x => x.Id);
                    table.ForeignKey(
                        name: "FK_models_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_models_depreciations_DepreciationId",
                        column: x => x.DepreciationId,
                        principalTable: "depreciations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_models_manufacturers_ManufacturerId",
                        column: x => x.ManufacturerId,
                        principalTable: "manufacturers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "group_permissions",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_permissions", x => new { x.GroupId, x.PermissionKey });
                    table.ForeignKey(
                        name: "FK_group_permissions_permission_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "permission_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "licenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Serial = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Seats = table.Column<int>(type: "integer", nullable: false),
                    Reassignable = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "true"),
                    ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TerminationDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PurchaseCost = table.Column<decimal>(type: "numeric", nullable: true),
                    PurchaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OrderNumber = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    MinSeats = table.Column<int>(type: "integer", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManufacturerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_licenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_licenses_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_licenses_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_licenses_manufacturers_ManufacturerId",
                        column: x => x.ManufacturerId,
                        principalTable: "manufacturers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_licenses_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "system_positions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SystemInfoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_positions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_system_positions_system_infos_SystemInfoId",
                        column: x => x.SystemInfoId,
                        principalTable: "system_infos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accessories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ItemNo = table.Column<string>(type: "text", nullable: true),
                    Image = table.Column<string>(type: "text", nullable: true),
                    ModelNumber = table.Column<string>(type: "text", nullable: true),
                    OrderNumber = table.Column<string>(type: "text", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManufacturerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Qty = table.Column<int>(type: "integer", nullable: false),
                    MinAmt = table.Column<int>(type: "integer", nullable: false),
                    PurchaseCost = table.Column<decimal>(type: "numeric", nullable: true),
                    PurchaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accessories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accessories_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_accessories_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_accessories_manufacturers_ManufacturerId",
                        column: x => x.ManufacturerId,
                        principalTable: "manufacturers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_accessories_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "accessory_checkouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AccessoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckoutType = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "1"),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AssignedQty = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "1"),
                    ReturnedQty = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CheckedOutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accessory_checkouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accessory_checkouts_accessories_AccessoryId",
                        column: x => x.AccessoryId,
                        principalTable: "accessories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "action_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ItemType = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetType = table.Column<int>(type: "integer", nullable: true),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActionType = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    LogMeta = table.Column<string>(type: "text", nullable: true),
                    ActionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RemoteIp = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    ActionSource = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: true),
                    FilePath = table.Column<string>(type: "text", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LocationName = table.Column<string>(type: "text", nullable: true),
                    TargetSystemInfoName = table.Column<string>(type: "text", nullable: true),
                    TargetSystemInfoId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "asset_maintenance_assignees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    MaintenanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asset_maintenance_assignees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "asset_maintenances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "'00000000-0000-0000-0000-000000000000'::uuid"),
                    StartDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CompletionDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Cost = table.Column<decimal>(type: "numeric", nullable: true),
                    IsWarranty = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    ClosedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ClosedById = table.Column<Guid>(type: "uuid", nullable: true),
                    InspectedById = table.Column<Guid>(type: "uuid", nullable: true),
                    InspectedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SnapshotSystemInfoId = table.Column<Guid>(type: "uuid", nullable: true),
                    SnapshotSystemInfoName = table.Column<string>(type: "text", nullable: true),
                    SnapshotSystemPositionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SnapshotSystemPositionName = table.Column<string>(type: "text", nullable: true),
                    SnapshotLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SnapshotLocationName = table.Column<string>(type: "text", nullable: true),
                    SnapshotAssignedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SnapshotAssignedUserName = table.Column<string>(type: "text", nullable: true),
                    SnapshotDepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SnapshotDepartmentName = table.Column<string>(type: "text", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asset_maintenances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_asset_maintenances_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AssetTag = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Serial = table.Column<string>(type: "text", nullable: true),
                    Image = table.Column<string>(type: "text", nullable: true),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentAssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SystemPositionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    IsConfirmed = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    PurchaseCost = table.Column<decimal>(type: "numeric", nullable: true),
                    PurchaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WarrantyMonths = table.Column<int>(type: "integer", nullable: true),
                    AssetEolDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EolExplicit = table.Column<bool>(type: "boolean", nullable: false),
                    LastCheckout = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCheckin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAuditDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAuditDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckinCounter = table.Column<int>(type: "integer", nullable: false),
                    CheckoutCounter = table.Column<int>(type: "integer", nullable: false),
                    RequestsCounter = table.Column<int>(type: "integer", nullable: false),
                    Physical = table.Column<bool>(type: "boolean", nullable: false),
                    Requestable = table.Column<bool>(type: "boolean", nullable: false),
                    Accepted = table.Column<string>(type: "text", nullable: true),
                    OrderNumber = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_assets_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_assets_models_ModelId",
                        column: x => x.ModelId,
                        principalTable: "models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_assets_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_assets_system_positions_SystemPositionId",
                        column: x => x.SystemPositionId,
                        principalTable: "system_positions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetType = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedById = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_assignments_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "component_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ComponentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedQty = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_component_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_component_assignments_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "component_units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ComponentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SerialNo = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    CurrentAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_component_units", x => x.Id);
                    table.ForeignKey(
                        name: "FK_component_units_assets_CurrentAssetId",
                        column: x => x.CurrentAssetId,
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "components",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Serial = table.Column<string>(type: "text", nullable: true),
                    ItemNo = table.Column<string>(type: "text", nullable: true),
                    Qty = table.Column<int>(type: "integer", nullable: false),
                    MinAmt = table.Column<int>(type: "integer", nullable: false),
                    PurchaseCost = table.Column<decimal>(type: "numeric", nullable: true),
                    PurchaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OrderNumber = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TrackingType = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManufacturerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModelNumber = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_components", x => x.Id);
                    table.ForeignKey(
                        name: "FK_components_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_components_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_components_manufacturers_ManufacturerId",
                        column: x => x.ManufacturerId,
                        principalTable: "manufacturers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_components_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "consumable_checkouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ConsumableId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedToId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CheckedOutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumable_checkouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "consumables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ItemNo = table.Column<string>(type: "text", nullable: true),
                    Image = table.Column<string>(type: "text", nullable: true),
                    ModelNumber = table.Column<string>(type: "text", nullable: true),
                    OrderNumber = table.Column<string>(type: "text", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManufacturerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Qty = table.Column<int>(type: "integer", nullable: false),
                    MinAmt = table.Column<int>(type: "integer", nullable: false),
                    PurchaseCost = table.Column<decimal>(type: "numeric", nullable: true),
                    PurchaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "1"),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_consumables_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_consumables_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_consumables_manufacturers_ManufacturerId",
                        column: x => x.ManufacturerId,
                        principalTable: "manufacturers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_consumables_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Fax = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_departments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_departments_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "license_seats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeatNumber = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    SystemPositionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_license_seats", x => x.Id);
                    table.CheckConstraint("CK_license_seats_single_target", "(((\"UserId\" IS NOT NULL)::int + (\"AssetId\" IS NOT NULL)::int + (\"SystemPositionId\" IS NOT NULL)::int) <= 1)");
                    table.ForeignKey(
                        name: "FK_license_seats_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_license_seats_licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_license_seats_system_positions_SystemPositionId",
                        column: x => x.SystemPositionId,
                        principalTable: "system_positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    Zip = table.Column<string>(type: "text", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_locations_locations_ParentId",
                        column: x => x.ParentId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EmployeeNumber = table.Column<string>(type: "text", nullable: true),
                    JobTitle = table.Column<string>(type: "text", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsSuperUser = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_users_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_users_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_users_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "user_groups",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_groups", x => new { x.UserId, x.GroupId });
                    table.ForeignKey(
                        name: "FK_user_groups_permission_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "permission_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_groups_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_permissions",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_permissions", x => new { x.UserId, x.PermissionKey });
                    table.ForeignKey(
                        name: "FK_user_permissions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accessories_CategoryId",
                table: "accessories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_accessories_CompanyId",
                table: "accessories",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_accessories_ItemNo",
                table: "accessories",
                column: "ItemNo");

            migrationBuilder.CreateIndex(
                name: "IX_accessories_LocationId",
                table: "accessories",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_accessories_ManufacturerId",
                table: "accessories",
                column: "ManufacturerId");

            migrationBuilder.CreateIndex(
                name: "IX_accessories_SupplierId",
                table: "accessories",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_accessory_checkouts_AccessoryId",
                table: "accessory_checkouts",
                column: "AccessoryId");

            migrationBuilder.CreateIndex(
                name: "IX_accessory_checkouts_CreatedByUserId",
                table: "accessory_checkouts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_action_logs_AssetId",
                table: "action_logs",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_action_logs_CreatedBy",
                table: "action_logs",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_action_logs_ItemType_ItemId",
                table: "action_logs",
                columns: new[] { "ItemType", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_action_logs_TargetSystemInfoId",
                table: "action_logs",
                column: "TargetSystemInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_asset_maintenance_assignees_MaintenanceId",
                table: "asset_maintenance_assignees",
                column: "MaintenanceId");

            migrationBuilder.CreateIndex(
                name: "IX_asset_maintenance_assignees_MaintenanceId_UserId",
                table: "asset_maintenance_assignees",
                columns: new[] { "MaintenanceId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_asset_maintenance_assignees_UserId",
                table: "asset_maintenance_assignees",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_asset_maintenances_AssetId",
                table: "asset_maintenances",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_asset_maintenances_CompanyId",
                table: "asset_maintenances",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_asset_maintenances_InspectedById",
                table: "asset_maintenances",
                column: "InspectedById");

            migrationBuilder.CreateIndex(
                name: "IX_asset_maintenances_SnapshotSystemInfoId",
                table: "asset_maintenances",
                column: "SnapshotSystemInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_asset_maintenances_SnapshotSystemPositionId",
                table: "asset_maintenances",
                column: "SnapshotSystemPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_asset_maintenances_SupplierId",
                table: "asset_maintenances",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_assets_AssetTag",
                table: "assets",
                column: "AssetTag",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assets_CompanyId",
                table: "assets",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_assets_CurrentAssignmentId",
                table: "assets",
                column: "CurrentAssignmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assets_LocationId",
                table: "assets",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_assets_ModelId",
                table: "assets",
                column: "ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_assets_Serial",
                table: "assets",
                column: "Serial");

            migrationBuilder.CreateIndex(
                name: "IX_assets_SupplierId",
                table: "assets",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_assets_SystemPositionId",
                table: "assets",
                column: "SystemPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_AssetId",
                table: "assignments",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_AssignedById",
                table: "assignments",
                column: "AssignedById");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_TargetId",
                table: "assignments",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_TargetType_TargetId",
                table: "assignments",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_companies_ParentId",
                table: "companies",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_component_assignments_AssetId",
                table: "component_assignments",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_component_assignments_ComponentId",
                table: "component_assignments",
                column: "ComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_component_units_ComponentId",
                table: "component_units",
                column: "ComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_component_units_CurrentAssetId",
                table: "component_units",
                column: "CurrentAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_component_units_SerialNo",
                table: "component_units",
                column: "SerialNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_component_units_Status",
                table: "component_units",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_components_CategoryId",
                table: "components",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_components_CompanyId",
                table: "components",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_components_LocationId",
                table: "components",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_components_ManufacturerId",
                table: "components",
                column: "ManufacturerId");

            migrationBuilder.CreateIndex(
                name: "IX_components_Serial",
                table: "components",
                column: "Serial");

            migrationBuilder.CreateIndex(
                name: "IX_components_SupplierId",
                table: "components",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_consumable_checkouts_ConsumableId",
                table: "consumable_checkouts",
                column: "ConsumableId");

            migrationBuilder.CreateIndex(
                name: "IX_consumable_checkouts_CreatedByUserId",
                table: "consumable_checkouts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_consumable_checkouts_UserId",
                table: "consumable_checkouts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_consumables_CategoryId",
                table: "consumables",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_consumables_CompanyId",
                table: "consumables",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_consumables_ItemNo",
                table: "consumables",
                column: "ItemNo");

            migrationBuilder.CreateIndex(
                name: "IX_consumables_LocationId",
                table: "consumables",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_consumables_ManufacturerId",
                table: "consumables",
                column: "ManufacturerId");

            migrationBuilder.CreateIndex(
                name: "IX_consumables_SupplierId",
                table: "consumables",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_fieldsets_FieldId",
                table: "custom_field_fieldsets",
                column: "FieldId");

            migrationBuilder.CreateIndex(
                name: "IX_custom_fields_Slug",
                table: "custom_fields",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_departments_CompanyId",
                table: "departments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_departments_ManagerId",
                table: "departments",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_departments_Name",
                table: "departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_license_seats_AssetId",
                table: "license_seats",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_license_seats_LicenseId",
                table: "license_seats",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_license_seats_LicenseId_SeatNumber",
                table: "license_seats",
                columns: new[] { "LicenseId", "SeatNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_license_seats_SystemPositionId",
                table: "license_seats",
                column: "SystemPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_license_seats_UserId",
                table: "license_seats",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_licenses_CategoryId",
                table: "licenses",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_licenses_CompanyId",
                table: "licenses",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_licenses_ManufacturerId",
                table: "licenses",
                column: "ManufacturerId");

            migrationBuilder.CreateIndex(
                name: "IX_licenses_Name",
                table: "licenses",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_licenses_SupplierId",
                table: "licenses",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_locations_ManagerId",
                table: "locations",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_locations_ParentId",
                table: "locations",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_manufacturers_Code",
                table: "manufacturers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_manufacturers_Name",
                table: "manufacturers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_models_CategoryId",
                table: "models",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_models_DepreciationId",
                table: "models",
                column: "DepreciationId");

            migrationBuilder.CreateIndex(
                name: "IX_models_ManufacturerId",
                table: "models",
                column: "ManufacturerId");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_Code",
                table: "suppliers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_Name",
                table: "suppliers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_infos_Code",
                table: "system_infos",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_infos_CompanyId",
                table: "system_infos",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_system_positions_Code",
                table: "system_positions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_positions_SystemInfoId",
                table: "system_positions",
                column: "SystemInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_user_groups_GroupId",
                table: "user_groups",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_users_CompanyId",
                table: "users",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_users_DepartmentId",
                table: "users",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_LocationId",
                table: "users",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_users_Username",
                table: "users",
                column: "Username",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_accessories_locations_LocationId",
                table: "accessories",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_accessory_checkouts_users_CreatedByUserId",
                table: "accessory_checkouts",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_action_logs_assets_AssetId",
                table: "action_logs",
                column: "AssetId",
                principalTable: "assets",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_action_logs_users_CreatedBy",
                table: "action_logs",
                column: "CreatedBy",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_asset_maintenance_assignees_asset_maintenances_MaintenanceId",
                table: "asset_maintenance_assignees",
                column: "MaintenanceId",
                principalTable: "asset_maintenances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_asset_maintenance_assignees_users_UserId",
                table: "asset_maintenance_assignees",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_asset_maintenances_assets_AssetId",
                table: "asset_maintenances",
                column: "AssetId",
                principalTable: "assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_asset_maintenances_users_InspectedById",
                table: "asset_maintenances",
                column: "InspectedById",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_assets_assignments_CurrentAssignmentId",
                table: "assets",
                column: "CurrentAssignmentId",
                principalTable: "assignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_assets_locations_LocationId",
                table: "assets",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_assignments_users_AssignedById",
                table: "assignments",
                column: "AssignedById",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_component_assignments_components_ComponentId",
                table: "component_assignments",
                column: "ComponentId",
                principalTable: "components",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_component_units_components_ComponentId",
                table: "component_units",
                column: "ComponentId",
                principalTable: "components",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_components_locations_LocationId",
                table: "components",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_consumable_checkouts_consumables_ConsumableId",
                table: "consumable_checkouts",
                column: "ConsumableId",
                principalTable: "consumables",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_consumable_checkouts_users_CreatedByUserId",
                table: "consumable_checkouts",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_consumable_checkouts_users_UserId",
                table: "consumable_checkouts",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_consumables_locations_LocationId",
                table: "consumables",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_departments_users_ManagerId",
                table: "departments",
                column: "ManagerId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_license_seats_users_UserId",
                table: "license_seats",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_locations_users_ManagerId",
                table: "locations",
                column: "ManagerId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_models_categories_CategoryId",
                table: "models");

            migrationBuilder.DropForeignKey(
                name: "FK_assets_companies_CompanyId",
                table: "assets");

            migrationBuilder.DropForeignKey(
                name: "FK_departments_companies_CompanyId",
                table: "departments");

            migrationBuilder.DropForeignKey(
                name: "FK_system_infos_companies_CompanyId",
                table: "system_infos");

            migrationBuilder.DropForeignKey(
                name: "FK_users_companies_CompanyId",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "FK_assets_locations_LocationId",
                table: "assets");

            migrationBuilder.DropForeignKey(
                name: "FK_users_locations_LocationId",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "FK_models_manufacturers_ManufacturerId",
                table: "models");

            migrationBuilder.DropForeignKey(
                name: "FK_assets_suppliers_SupplierId",
                table: "assets");

            migrationBuilder.DropForeignKey(
                name: "FK_assignments_users_AssignedById",
                table: "assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_departments_users_ManagerId",
                table: "departments");

            migrationBuilder.DropForeignKey(
                name: "FK_assignments_assets_AssetId",
                table: "assignments");

            migrationBuilder.DropTable(
                name: "accessory_checkouts");

            migrationBuilder.DropTable(
                name: "action_logs");

            migrationBuilder.DropTable(
                name: "asset_maintenance_assignees");

            migrationBuilder.DropTable(
                name: "component_assignments");

            migrationBuilder.DropTable(
                name: "component_units");

            migrationBuilder.DropTable(
                name: "consumable_checkouts");

            migrationBuilder.DropTable(
                name: "custom_field_fieldsets");

            migrationBuilder.DropTable(
                name: "group_permissions");

            migrationBuilder.DropTable(
                name: "license_seats");

            migrationBuilder.DropTable(
                name: "status_labels");

            migrationBuilder.DropTable(
                name: "user_groups");

            migrationBuilder.DropTable(
                name: "user_permissions");

            migrationBuilder.DropTable(
                name: "accessories");

            migrationBuilder.DropTable(
                name: "asset_maintenances");

            migrationBuilder.DropTable(
                name: "components");

            migrationBuilder.DropTable(
                name: "consumables");

            migrationBuilder.DropTable(
                name: "custom_fields");

            migrationBuilder.DropTable(
                name: "custom_fieldsets");

            migrationBuilder.DropTable(
                name: "licenses");

            migrationBuilder.DropTable(
                name: "permission_groups");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "companies");

            migrationBuilder.DropTable(
                name: "locations");

            migrationBuilder.DropTable(
                name: "manufacturers");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "departments");

            migrationBuilder.DropTable(
                name: "assets");

            migrationBuilder.DropTable(
                name: "assignments");

            migrationBuilder.DropTable(
                name: "models");

            migrationBuilder.DropTable(
                name: "system_positions");

            migrationBuilder.DropTable(
                name: "depreciations");

            migrationBuilder.DropTable(
                name: "system_infos");
        }
    }
}
