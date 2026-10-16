using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddAllocationPlan : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AllocationBuckets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
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
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AllocationPlanVersions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AllocationPlanEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                BucketId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ShareType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Share = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Position = table.Column<int>(type: "integer", nullable: false),
                IsRemainder = table.Column<bool>(type: "boolean", nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: true)
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
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AllocationPlanEntries");

        migrationBuilder.DropTable(
            name: "AllocationBuckets");

        migrationBuilder.DropTable(
            name: "AllocationPlanVersions");
    }
}
