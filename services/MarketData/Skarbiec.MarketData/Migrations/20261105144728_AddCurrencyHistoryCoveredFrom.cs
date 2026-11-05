using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.MarketData.Migrations;

/// <inheritdoc />
public partial class AddCurrencyHistoryCoveredFrom : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "HistoryCoveredFrom",
            table: "Currencies",
            type: "date",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "HistoryCoveredFrom",
            table: "Currencies");
    }
}
