using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class OneOffIncomeSettlement : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ActualAccountId",
            table: "OneOffIncomes",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ActualAmount",
            table: "OneOffIncomes",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "ActualDate",
            table: "OneOffIncomes",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "FxRateToPln",
            table: "OneOffIncomes",
            type: "numeric(18,6)",
            precision: 18,
            scale: 6,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "TransactionId",
            table: "OneOffIncomes",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_OneOffIncomes_ActualAccountId",
            table: "OneOffIncomes",
            column: "ActualAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_OneOffIncomes_TransactionId",
            table: "OneOffIncomes",
            column: "TransactionId");

        migrationBuilder.AddForeignKey(
            name: "FK_OneOffIncomes_Accounts_ActualAccountId",
            table: "OneOffIncomes",
            column: "ActualAccountId",
            principalTable: "Accounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_OneOffIncomes_Transactions_TransactionId",
            table: "OneOffIncomes",
            column: "TransactionId",
            principalTable: "Transactions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_OneOffIncomes_Accounts_ActualAccountId",
            table: "OneOffIncomes");

        migrationBuilder.DropForeignKey(
            name: "FK_OneOffIncomes_Transactions_TransactionId",
            table: "OneOffIncomes");

        migrationBuilder.DropIndex(
            name: "IX_OneOffIncomes_ActualAccountId",
            table: "OneOffIncomes");

        migrationBuilder.DropIndex(
            name: "IX_OneOffIncomes_TransactionId",
            table: "OneOffIncomes");

        migrationBuilder.DropColumn(
            name: "ActualAccountId",
            table: "OneOffIncomes");

        migrationBuilder.DropColumn(
            name: "ActualAmount",
            table: "OneOffIncomes");

        migrationBuilder.DropColumn(
            name: "ActualDate",
            table: "OneOffIncomes");

        migrationBuilder.DropColumn(
            name: "FxRateToPln",
            table: "OneOffIncomes");

        migrationBuilder.DropColumn(
            name: "TransactionId",
            table: "OneOffIncomes");
    }
}
