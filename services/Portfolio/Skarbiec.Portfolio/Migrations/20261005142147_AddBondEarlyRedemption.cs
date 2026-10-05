using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddBondEarlyRedemption : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BondRedemptions_AssetId",
            table: "BondRedemptions");

        migrationBuilder.AddColumn<decimal>(
            name: "AccruedInterest",
            table: "BondRedemptions",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "Fee",
            table: "BondRedemptions",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.CreateIndex(
            name: "IX_BondRedemptions_AssetId",
            table: "BondRedemptions",
            column: "AssetId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BondRedemptions_AssetId",
            table: "BondRedemptions");

        migrationBuilder.DropColumn(
            name: "AccruedInterest",
            table: "BondRedemptions");

        migrationBuilder.DropColumn(
            name: "Fee",
            table: "BondRedemptions");

        migrationBuilder.CreateIndex(
            name: "IX_BondRedemptions_AssetId",
            table: "BondRedemptions",
            column: "AssetId",
            unique: true);
    }
}
