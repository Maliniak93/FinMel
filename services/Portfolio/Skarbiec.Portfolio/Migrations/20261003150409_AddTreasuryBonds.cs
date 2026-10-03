using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddTreasuryBonds : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TreasuryBonds",
            columns: table => new
            {
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                SeriesCode = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                PurchaseDate = table.Column<DateOnly>(type: "date", nullable: false),
                BondCount = table.Column<int>(type: "integer", nullable: false),
                PurchasePricePerBond = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                FirstPeriodRatePercent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                MarginPercent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: true),
                EarlyRedemptionFeePerBond = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                TaxExempt = table.Column<bool>(type: "boolean", nullable: false),
                MaturityDate = table.Column<DateOnly>(type: "date", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TreasuryBonds", x => x.AssetId);
                table.ForeignKey(
                    name: "FK_TreasuryBonds_Assets_AssetId",
                    column: x => x.AssetId,
                    principalTable: "Assets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TreasuryBonds");
    }
}
