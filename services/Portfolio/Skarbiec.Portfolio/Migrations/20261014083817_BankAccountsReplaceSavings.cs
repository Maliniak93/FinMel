using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class BankAccountsReplaceSavings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SavingsAccounts");

        migrationBuilder.DropTable(
            name: "SavingsInterestSettlements");

        migrationBuilder.AddColumn<string>(
            name: "Currency",
            table: "Accounts",
            type: "character varying(3)",
            maxLength: 3,
            nullable: false,
            defaultValue: "PLN");

        migrationBuilder.CreateTable(
            name: "BankAccountTerms",
            columns: table => new
            {
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Variant = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                BankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                AnnualInterestRatePercent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BankAccountTerms", x => x.AccountId);
                table.ForeignKey(
                    name: "FK_BankAccountTerms_Accounts_AccountId",
                    column: x => x.AccountId,
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BankInterestSettlements",
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
                table.PrimaryKey("PK_BankInterestSettlements", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BankInterestSettlements_AssetId_PeriodEnd",
            table: "BankInterestSettlements",
            columns: new[] { "AssetId", "PeriodEnd" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BankInterestSettlements_TransactionId",
            table: "BankInterestSettlements",
            column: "TransactionId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BankAccountTerms");

        migrationBuilder.DropTable(
            name: "BankInterestSettlements");

        migrationBuilder.DropColumn(
            name: "Currency",
            table: "Accounts");

        migrationBuilder.CreateTable(
            name: "SavingsAccounts",
            columns: table => new
            {
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                AnnualInterestRatePercent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                BankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                TaxExempt = table.Column<bool>(type: "boolean", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SavingsAccounts", x => x.AssetId);
                table.ForeignKey(
                    name: "FK_SavingsAccounts_Assets_AssetId",
                    column: x => x.AssetId,
                    principalTable: "Assets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SavingsInterestSettlements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                GrossInterest = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                Tax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                TransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                UserId = table.Column<Guid>(type: "uuid", nullable: false)
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
}
