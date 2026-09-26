using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddTermDeposits : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TermDeposits",
            columns: table => new
            {
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                BankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                Principal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                TermLength = table.Column<int>(type: "integer", nullable: false),
                TermUnit = table.Column<int>(type: "integer", nullable: false),
                MaturityDate = table.Column<DateOnly>(type: "date", nullable: false),
                AnnualInterestRatePercent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                Capitalization = table.Column<int>(type: "integer", nullable: false),
                TaxExempt = table.Column<bool>(type: "boolean", nullable: false),
                EarlyBreakInterestLossPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TermDeposits", x => x.AssetId);
                table.ForeignKey(
                    name: "FK_TermDeposits_Assets_AssetId",
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
            name: "TermDeposits");
    }
}
