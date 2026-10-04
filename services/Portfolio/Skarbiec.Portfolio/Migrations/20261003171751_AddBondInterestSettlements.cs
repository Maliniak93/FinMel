using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddBondInterestSettlements : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BondInterestSettlements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                PeriodIndex = table.Column<int>(type: "integer", nullable: false),
                PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                RatePercent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                BondCount = table.Column<int>(type: "integer", nullable: false),
                GrossInterest = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Tax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                CreditTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                TransferId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BondInterestSettlements", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BondInterestSettlements_AssetId_PeriodIndex",
            table: "BondInterestSettlements",
            columns: new[] { "AssetId", "PeriodIndex" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BondInterestSettlements_CreditTransactionId",
            table: "BondInterestSettlements",
            column: "CreditTransactionId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BondInterestSettlements");
    }
}
