using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Reporting.Migrations;

/// <inheritdoc />
public partial class PositionsAndAssetValuations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "BreakdownJson",
            table: "ValuationSnapshots");

        migrationBuilder.CreateTable(
            name: "AssetValuations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                AssetClass = table.Column<int>(type: "integer", nullable: false),
                Quantity = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                PriceUsed = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                PriceDate = table.Column<DateOnly>(type: "date", nullable: true),
                FxRateUsed = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                ValuePln = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                IsStale = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssetValuations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Positions",
            columns: table => new
            {
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                AssetClass = table.Column<int>(type: "integer", nullable: false),
                ValuationMode = table.Column<int>(type: "integer", nullable: false),
                InstrumentId = table.Column<Guid>(type: "uuid", nullable: true),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Quantity = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                ManualValueAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                ManualValueDate = table.Column<DateOnly>(type: "date", nullable: true),
                PortfolioIsArchived = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Positions", x => x.AssetId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AssetValuations_AssetId_Date",
            table: "AssetValuations",
            columns: new[] { "AssetId", "Date" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AssetValuations_UserId_Date",
            table: "AssetValuations",
            columns: new[] { "UserId", "Date" });

        migrationBuilder.CreateIndex(
            name: "IX_Positions_PortfolioId",
            table: "Positions",
            column: "PortfolioId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AssetValuations");

        migrationBuilder.DropTable(
            name: "Positions");

        migrationBuilder.AddColumn<string>(
            name: "BreakdownJson",
            table: "ValuationSnapshots",
            type: "jsonb",
            nullable: false,
            defaultValue: "");
    }
}
