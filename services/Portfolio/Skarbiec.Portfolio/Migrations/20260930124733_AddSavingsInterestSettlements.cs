using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddSavingsInterestSettlements : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SavingsInterestSettlements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                GrossInterest = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Tax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                TransactionId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SavingsInterestSettlements", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SavingsInterestSettlements_AssetId_PeriodEnd",
            table: "SavingsInterestSettlements",
            columns: new[] { "AssetId", "PeriodEnd" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SavingsInterestSettlements_TransactionId",
            table: "SavingsInterestSettlements",
            column: "TransactionId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SavingsInterestSettlements");
    }
}
