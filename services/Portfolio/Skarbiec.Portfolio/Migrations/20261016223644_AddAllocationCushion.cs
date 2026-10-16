using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddAllocationCushion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "ShareType",
            table: "AllocationPlanEntries",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(20)",
            oldMaxLength: 20);

        migrationBuilder.AlterColumn<decimal>(
            name: "Share",
            table: "AllocationPlanEntries",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,2)",
            oldPrecision: 18,
            oldScale: 2);

        migrationBuilder.AddColumn<int>(
            name: "TargetMonths",
            table: "AllocationPlanEntries",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "AllocationBalanceOverrides",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                BucketId = table.Column<Guid>(type: "uuid", nullable: false),
                Month = table.Column<DateOnly>(type: "date", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
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

        migrationBuilder.CreateIndex(
            name: "IX_AllocationBalanceOverrides_BucketId_Month",
            table: "AllocationBalanceOverrides",
            columns: new[] { "BucketId", "Month" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AllocationBalanceOverrides");

        migrationBuilder.DropColumn(
            name: "TargetMonths",
            table: "AllocationPlanEntries");

        migrationBuilder.AlterColumn<string>(
            name: "ShareType",
            table: "AllocationPlanEntries",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(20)",
            oldMaxLength: 20,
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "Share",
            table: "AllocationPlanEntries",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,2)",
            oldPrecision: 18,
            oldScale: 2,
            oldNullable: true);
    }
}
