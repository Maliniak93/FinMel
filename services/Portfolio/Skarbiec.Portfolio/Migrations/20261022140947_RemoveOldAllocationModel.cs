using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class RemoveOldAllocationModel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AllocationBalanceOverrides");

        migrationBuilder.DropTable(
            name: "AllocationPlanEntries");

        migrationBuilder.DropTable(
            name: "TargetAllocationReplicas");

        migrationBuilder.DropTable(
            name: "TransferPlanRows");

        migrationBuilder.DropTable(
            name: "AllocationBuckets");

        migrationBuilder.DropTable(
            name: "AllocationPlanVersions");

        migrationBuilder.DropTable(
            name: "TransferPlanBatches");

        migrationBuilder.DropTable(
            name: "TransferPlans");

        migrationBuilder.DropColumn(
            name: "MinimumTransferPln",
            table: "PlanningSettings");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "MinimumTransferPln",
            table: "PlanningSettings",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "AllocationBuckets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AllocationBuckets", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AllocationPlanVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                ReserveAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                UserId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AllocationPlanVersions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "TargetAllocationReplicas",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                SavedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Groups = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TargetAllocationReplicas", x => x.UserId);
            });

        migrationBuilder.CreateTable(
            name: "TransferPlans",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                IsSkipped = table.Column<bool>(type: "boolean", nullable: false),
                Month = table.Column<DateOnly>(type: "date", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TransferPlans", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AllocationBalanceOverrides",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                BucketId = table.Column<Guid>(type: "uuid", nullable: false),
                Month = table.Column<DateOnly>(type: "date", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AllocationBalanceOverrides", x => x.Id);
                table.ForeignKey(
                    name: "FK_AllocationBalanceOverrides_AllocationBuckets_BucketId",
                    column: x => x.BucketId,
                    principalTable: "AllocationBuckets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AllocationPlanEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                BucketId = table.Column<Guid>(type: "uuid", nullable: false),
                IsRemainder = table.Column<bool>(type: "boolean", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Position = table.Column<int>(type: "integer", nullable: false),
                Share = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                ShareType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                TargetMonths = table.Column<int>(type: "integer", nullable: true),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                VersionId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AllocationPlanEntries", x => x.Id);
                table.ForeignKey(
                    name: "FK_AllocationPlanEntries_AllocationBuckets_BucketId",
                    column: x => x.BucketId,
                    principalTable: "AllocationBuckets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AllocationPlanEntries_AllocationPlanVersions_VersionId",
                    column: x => x.VersionId,
                    principalTable: "AllocationPlanVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TransferPlanBatches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AcceptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                MinimumTransferPln = table.Column<int>(type: "integer", nullable: false),
                Number = table.Column<int>(type: "integer", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ValuationDate = table.Column<DateOnly>(type: "date", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TransferPlanBatches", x => x.Id);
                table.ForeignKey(
                    name: "FK_TransferPlanBatches_TransferPlans_PlanId",
                    column: x => x.PlanId,
                    principalTable: "TransferPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TransferPlanRows",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                AccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                AmountPln = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                AssetClasses = table.Column<int[]>(type: "integer[]", nullable: false),
                BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                BucketId = table.Column<Guid>(type: "uuid", nullable: false),
                BucketName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CheckedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                GroupName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                Marker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Position = table.Column<int>(type: "integer", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TransferPlanRows", x => x.Id);
                table.ForeignKey(
                    name: "FK_TransferPlanRows_TransferPlanBatches_BatchId",
                    column: x => x.BatchId,
                    principalTable: "TransferPlanBatches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AllocationBalanceOverrides_BucketId_Month",
            table: "AllocationBalanceOverrides",
            columns: new[] { "BucketId", "Month" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AllocationPlanEntries_BucketId",
            table: "AllocationPlanEntries",
            column: "BucketId");

        migrationBuilder.CreateIndex(
            name: "IX_AllocationPlanEntries_VersionId",
            table: "AllocationPlanEntries",
            column: "VersionId");

        migrationBuilder.CreateIndex(
            name: "IX_AllocationPlanVersions_UserId_EffectiveFrom",
            table: "AllocationPlanVersions",
            columns: new[] { "UserId", "EffectiveFrom" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TransferPlanBatches_PlanId_Number",
            table: "TransferPlanBatches",
            columns: new[] { "PlanId", "Number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TransferPlanRows_BatchId",
            table: "TransferPlanRows",
            column: "BatchId");

        migrationBuilder.CreateIndex(
            name: "IX_TransferPlans_UserId_Month",
            table: "TransferPlans",
            columns: new[] { "UserId", "Month" },
            unique: true);
    }
}
