using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddBondRedemptions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "SwappedFromAssetId",
            table: "TreasuryBonds",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "BondRedemptions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<int>(type: "integer", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                BondCount = table.Column<int>(type: "integer", nullable: false),
                CapitalisedInterest = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                DiscountIncome = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Tax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Proceeds = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                CreditTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                ChargeTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                CashTransferId = table.Column<Guid>(type: "uuid", nullable: true),
                SwapTargetAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                SwapTransferId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BondRedemptions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BondRedemptions_AssetId",
            table: "BondRedemptions",
            column: "AssetId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BondRedemptions");

        migrationBuilder.DropColumn(
            name: "SwappedFromAssetId",
            table: "TreasuryBonds");
    }
}
