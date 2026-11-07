using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class OneOffExpenseSettlement : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "AccountId",
            table: "OneOffExpenses",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ActualAccountId",
            table: "OneOffExpenses",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ActualAmount",
            table: "OneOffExpenses",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "ActualDate",
            table: "OneOffExpenses",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "FxRateToPln",
            table: "OneOffExpenses",
            type: "numeric(18,6)",
            precision: 18,
            scale: 6,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "TransactionId",
            table: "OneOffExpenses",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_OneOffExpenses_AccountId",
            table: "OneOffExpenses",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_OneOffExpenses_ActualAccountId",
            table: "OneOffExpenses",
            column: "ActualAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_OneOffExpenses_TransactionId",
            table: "OneOffExpenses",
            column: "TransactionId");

        migrationBuilder.AddForeignKey(
            name: "FK_OneOffExpenses_Accounts_AccountId",
            table: "OneOffExpenses",
            column: "AccountId",
            principalTable: "Accounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_OneOffExpenses_Accounts_ActualAccountId",
            table: "OneOffExpenses",
            column: "ActualAccountId",
            principalTable: "Accounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_OneOffExpenses_Transactions_TransactionId",
            table: "OneOffExpenses",
            column: "TransactionId",
            principalTable: "Transactions",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_OneOffExpenses_Accounts_AccountId",
            table: "OneOffExpenses");

        migrationBuilder.DropForeignKey(
            name: "FK_OneOffExpenses_Accounts_ActualAccountId",
            table: "OneOffExpenses");

        migrationBuilder.DropForeignKey(
            name: "FK_OneOffExpenses_Transactions_TransactionId",
            table: "OneOffExpenses");

        migrationBuilder.DropIndex(
            name: "IX_OneOffExpenses_AccountId",
            table: "OneOffExpenses");

        migrationBuilder.DropIndex(
            name: "IX_OneOffExpenses_ActualAccountId",
            table: "OneOffExpenses");

        migrationBuilder.DropIndex(
            name: "IX_OneOffExpenses_TransactionId",
            table: "OneOffExpenses");

        migrationBuilder.DropColumn(
            name: "AccountId",
            table: "OneOffExpenses");

        migrationBuilder.DropColumn(
            name: "ActualAccountId",
            table: "OneOffExpenses");

        migrationBuilder.DropColumn(
            name: "ActualAmount",
            table: "OneOffExpenses");

        migrationBuilder.DropColumn(
            name: "ActualDate",
            table: "OneOffExpenses");

        migrationBuilder.DropColumn(
            name: "FxRateToPln",
            table: "OneOffExpenses");

        migrationBuilder.DropColumn(
            name: "TransactionId",
            table: "OneOffExpenses");
    }
}
