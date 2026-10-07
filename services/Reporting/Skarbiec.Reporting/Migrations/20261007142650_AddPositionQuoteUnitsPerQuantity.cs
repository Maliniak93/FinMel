using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Reporting.Migrations;

/// <inheritdoc />
public partial class AddPositionQuoteUnitsPerQuantity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "QuoteUnitsPerQuantity",
            table: "Positions",
            type: "numeric(18,8)",
            precision: 18,
            scale: 8,
            nullable: false,
            defaultValue: 1m);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "QuoteUnitsPerQuantity",
            table: "Positions");
    }
}
