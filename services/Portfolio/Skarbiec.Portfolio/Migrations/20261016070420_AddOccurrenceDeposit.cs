using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddOccurrenceDeposit : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "TransactionId",
            table: "RecurringIncomeOccurrences",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_RecurringIncomeOccurrences_TransactionId",
            table: "RecurringIncomeOccurrences",
            column: "TransactionId");

        migrationBuilder.AddForeignKey(
            name: "FK_RecurringIncomeOccurrences_Transactions_TransactionId",
            table: "RecurringIncomeOccurrences",
            column: "TransactionId",
            principalTable: "Transactions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_RecurringIncomeOccurrences_Transactions_TransactionId",
            table: "RecurringIncomeOccurrences");

        migrationBuilder.DropIndex(
            name: "IX_RecurringIncomeOccurrences_TransactionId",
            table: "RecurringIncomeOccurrences");

        migrationBuilder.DropColumn(
            name: "TransactionId",
            table: "RecurringIncomeOccurrences");
    }
}
