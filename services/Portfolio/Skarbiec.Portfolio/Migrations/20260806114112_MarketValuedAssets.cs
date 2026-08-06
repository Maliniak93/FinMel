using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class MarketValuedAssets : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<DateOnly>(
            name: "ManualValueDate",
            table: "Assets",
            type: "date",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "date");

        migrationBuilder.AlterColumn<decimal>(
            name: "ManualValueAmount",
            table: "Assets",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,2)",
            oldPrecision: 18,
            oldScale: 2);

        migrationBuilder.CreateIndex(
            name: "IX_Assets_InstrumentId",
            table: "Assets",
            column: "InstrumentId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Assets_InstrumentId",
            table: "Assets");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "ManualValueDate",
            table: "Assets",
            type: "date",
            nullable: false,
            defaultValue: new DateOnly(1, 1, 1),
            oldClrType: typeof(DateOnly),
            oldType: "date",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "ManualValueAmount",
            table: "Assets",
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
