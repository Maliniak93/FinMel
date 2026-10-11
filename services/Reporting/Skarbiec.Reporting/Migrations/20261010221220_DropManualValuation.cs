using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Reporting.Migrations;

/// <inheritdoc />
public partial class DropManualValuation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ManualValueAmount",
            table: "Positions");

        migrationBuilder.DropColumn(
            name: "ManualValueDate",
            table: "Positions");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "ManualValueAmount",
            table: "Positions",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "ManualValueDate",
            table: "Positions",
            type: "date",
            nullable: true);
    }
}
