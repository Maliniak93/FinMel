using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class DropManualValuation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ManualValueAmount",
            table: "Assets");

        migrationBuilder.DropColumn(
            name: "ManualValueDate",
            table: "Assets");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "ManualValueAmount",
            table: "Assets",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "ManualValueDate",
            table: "Assets",
            type: "date",
            nullable: true);
    }
}
