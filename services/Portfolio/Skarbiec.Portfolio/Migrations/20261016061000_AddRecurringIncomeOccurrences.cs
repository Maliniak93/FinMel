using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddRecurringIncomeOccurrences : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RecurringIncomeOccurrences",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                RecurringIncomeId = table.Column<Guid>(type: "uuid", nullable: false),
                NominalMonth = table.Column<DateOnly>(type: "date", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                PlannedDate = table.Column<DateOnly>(type: "date", nullable: false),
                PlannedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                PlannedAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                ActualAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                ActualDate = table.Column<DateOnly>(type: "date", nullable: true),
                AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                FxRateToPln = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RecurringIncomeOccurrences", x => x.Id);
                table.ForeignKey(
                    name: "FK_RecurringIncomeOccurrences_Accounts_AccountId",
                    column: x => x.AccountId,
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_RecurringIncomeOccurrences_Accounts_PlannedAccountId",
                    column: x => x.PlannedAccountId,
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_RecurringIncomeOccurrences_RecurringIncomes_RecurringIncome~",
                    column: x => x.RecurringIncomeId,
                    principalTable: "RecurringIncomes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RecurringIncomeOccurrences_AccountId",
            table: "RecurringIncomeOccurrences",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_RecurringIncomeOccurrences_PlannedAccountId",
            table: "RecurringIncomeOccurrences",
            column: "PlannedAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_RecurringIncomeOccurrences_RecurringIncomeId_NominalMonth",
            table: "RecurringIncomeOccurrences",
            columns: new[] { "RecurringIncomeId", "NominalMonth" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RecurringIncomeOccurrences");
    }
}
