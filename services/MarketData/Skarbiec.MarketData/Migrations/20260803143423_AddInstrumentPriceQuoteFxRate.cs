using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.MarketData.Migrations;

/// <inheritdoc />
public partial class AddInstrumentPriceQuoteFxRate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FxRates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Pair = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                Rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FxRates", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Instruments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Ticker = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Source = table.Column<int>(type: "integer", nullable: false),
                QuoteCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                AssetClass = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Instruments", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "PriceQuotes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                Close = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PriceQuotes", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FxRates_Pair_Date",
            table: "FxRates",
            columns: new[] { "Pair", "Date" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Instruments_Source_Ticker",
            table: "Instruments",
            columns: new[] { "Source", "Ticker" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PriceQuotes_InstrumentId_Date",
            table: "PriceQuotes",
            columns: new[] { "InstrumentId", "Date" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "FxRates");

        migrationBuilder.DropTable(
            name: "Instruments");

        migrationBuilder.DropTable(
            name: "PriceQuotes");
    }
}
