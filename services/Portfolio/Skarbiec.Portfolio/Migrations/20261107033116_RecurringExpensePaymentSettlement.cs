using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class RecurringExpensePaymentSettlement : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "AccountId",
            table: "RecurringExpenseVersions",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "RecurringExpenseOccurrences",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                RecurringExpenseId = table.Column<Guid>(type: "uuid", nullable: false),
                NominalMonth = table.Column<DateOnly>(type: "date", nullable: false),
                PlannedDate = table.Column<DateOnly>(type: "date", nullable: false),
                PlannedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                ActualAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                ActualDate = table.Column<DateOnly>(type: "date", nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                FxRateToPln = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                TransactionId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RecurringExpenseOccurrences", x => x.Id);
                table.ForeignKey(
                    name: "FK_RecurringExpenseOccurrences_Accounts_AccountId",
                    column: x => x.AccountId,
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_RecurringExpenseOccurrences_RecurringExpenses_RecurringExpe~",
                    column: x => x.RecurringExpenseId,
                    principalTable: "RecurringExpenses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_RecurringExpenseOccurrences_Transactions_TransactionId",
                    column: x => x.TransactionId,
                    principalTable: "Transactions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RecurringExpenseVersions_AccountId",
            table: "RecurringExpenseVersions",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_RecurringExpenseOccurrences_AccountId",
            table: "RecurringExpenseOccurrences",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_RecurringExpenseOccurrences_RecurringExpenseId_NominalMonth",
            table: "RecurringExpenseOccurrences",
            columns: new[] { "RecurringExpenseId", "NominalMonth" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RecurringExpenseOccurrences_TransactionId",
            table: "RecurringExpenseOccurrences",
            column: "TransactionId");

        migrationBuilder.AddForeignKey(
            name: "FK_RecurringExpenseVersions_Accounts_AccountId",
            table: "RecurringExpenseVersions",
            column: "AccountId",
            principalTable: "Accounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_RecurringExpenseVersions_Accounts_AccountId",
            table: "RecurringExpenseVersions");

        migrationBuilder.DropTable(
            name: "RecurringExpenseOccurrences");

        migrationBuilder.DropIndex(
            name: "IX_RecurringExpenseVersions_AccountId",
            table: "RecurringExpenseVersions");

        migrationBuilder.DropColumn(
            name: "AccountId",
            table: "RecurringExpenseVersions");
    }
}
