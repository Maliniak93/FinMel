using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Reporting.Migrations;

/// <inheritdoc />
public partial class AddLatestPricesAndFxRates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LatestFxRates",
            columns: table => new
            {
                Pair = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                Rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LatestFxRates", x => x.Pair);
            });

        migrationBuilder.CreateTable(
            name: "LatestInstrumentPrices",
            columns: table => new
            {
                InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                QuoteCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                Close = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LatestInstrumentPrices", x => x.InstrumentId);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "LatestFxRates");

        migrationBuilder.DropTable(
            name: "LatestInstrumentPrices");
    }
}
