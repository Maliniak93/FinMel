using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMainDashboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankAccount_Currency_CurrencyId",
                table: "BankAccount");

            migrationBuilder.DropForeignKey(
                name: "FK_BankStatement_BankAccount_BankAccountId",
                table: "BankStatement");

            migrationBuilder.DropForeignKey(
                name: "FK_BankStatement_StatementFile_StatementFileId",
                table: "BankStatement");

            migrationBuilder.DropForeignKey(
                name: "FK_History_BankAccount_BankAccountId",
                table: "History");

            migrationBuilder.DropForeignKey(
                name: "FK_StatementTransaction_BankStatement_BankStatementId",
                table: "StatementTransaction");

            migrationBuilder.DropForeignKey(
                name: "FK_StatementTransaction_TransactionCode_TransactionCodeId",
                table: "StatementTransaction");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TransactionCode",
                table: "TransactionCode");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StatementTransaction",
                table: "StatementTransaction");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StatementFile",
                table: "StatementFile");

            migrationBuilder.DropPrimaryKey(
                name: "PK_History",
                table: "History");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Currency",
                table: "Currency");

            migrationBuilder.DropPrimaryKey(
                name: "PK_BankStatement",
                table: "BankStatement");

            migrationBuilder.DropPrimaryKey(
                name: "PK_BankAccount",
                table: "BankAccount");

            migrationBuilder.RenameTable(
                name: "TransactionCode",
                newName: "TransactionCodes");

            migrationBuilder.RenameTable(
                name: "StatementTransaction",
                newName: "StatementTransactions");

            migrationBuilder.RenameTable(
                name: "StatementFile",
                newName: "StatementFiles");

            migrationBuilder.RenameTable(
                name: "History",
                newName: "Histories");

            migrationBuilder.RenameTable(
                name: "Currency",
                newName: "CurrencyCodes");

            migrationBuilder.RenameTable(
                name: "BankStatement",
                newName: "BankStatements");

            migrationBuilder.RenameTable(
                name: "BankAccount",
                newName: "BankAccounts");

            migrationBuilder.RenameIndex(
                name: "IX_TransactionCode_Code",
                table: "TransactionCodes",
                newName: "IX_TransactionCodes_Code");

            migrationBuilder.RenameIndex(
                name: "IX_StatementTransaction_TransactionCodeId",
                table: "StatementTransactions",
                newName: "IX_StatementTransactions_TransactionCodeId");

            migrationBuilder.RenameIndex(
                name: "IX_StatementTransaction_BankStatementId",
                table: "StatementTransactions",
                newName: "IX_StatementTransactions_BankStatementId");

            migrationBuilder.RenameIndex(
                name: "IX_History_BankAccountId",
                table: "Histories",
                newName: "IX_Histories_BankAccountId");

            migrationBuilder.RenameIndex(
                name: "IX_BankStatement_StatementFileId",
                table: "BankStatements",
                newName: "IX_BankStatements_StatementFileId");

            migrationBuilder.RenameIndex(
                name: "IX_BankStatement_BankAccountId",
                table: "BankStatements",
                newName: "IX_BankStatements_BankAccountId");

            migrationBuilder.RenameIndex(
                name: "IX_BankAccount_CurrencyId",
                table: "BankAccounts",
                newName: "IX_BankAccounts_CurrencyId");

            migrationBuilder.RenameIndex(
                name: "IX_BankAccount_AccountNumber",
                table: "BankAccounts",
                newName: "IX_BankAccounts_AccountNumber");

            migrationBuilder.AddColumn<int>(
                name: "MainDashboardId",
                table: "BankAccounts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_TransactionCodes",
                table: "TransactionCodes",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StatementTransactions",
                table: "StatementTransactions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StatementFiles",
                table: "StatementFiles",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Histories",
                table: "Histories",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CurrencyCodes",
                table: "CurrencyCodes",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_BankStatements",
                table: "BankStatements",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_BankAccounts",
                table: "BankAccounts",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "MainDashboard",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonalWealth = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    MonthlyExpenses = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    AverageMonthlyExpense = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    MonthlyIncome = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    AverageMonthlyIncome = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MainDashboard", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_MainDashboardId",
                table: "BankAccounts",
                column: "MainDashboardId");

            migrationBuilder.AddForeignKey(
                name: "FK_BankAccounts_CurrencyCodes_CurrencyId",
                table: "BankAccounts",
                column: "CurrencyId",
                principalTable: "CurrencyCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BankAccounts_MainDashboard_MainDashboardId",
                table: "BankAccounts",
                column: "MainDashboardId",
                principalTable: "MainDashboard",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BankStatements_BankAccounts_BankAccountId",
                table: "BankStatements",
                column: "BankAccountId",
                principalTable: "BankAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BankStatements_StatementFiles_StatementFileId",
                table: "BankStatements",
                column: "StatementFileId",
                principalTable: "StatementFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Histories_BankAccounts_BankAccountId",
                table: "Histories",
                column: "BankAccountId",
                principalTable: "BankAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StatementTransactions_BankStatements_BankStatementId",
                table: "StatementTransactions",
                column: "BankStatementId",
                principalTable: "BankStatements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StatementTransactions_TransactionCodes_TransactionCodeId",
                table: "StatementTransactions",
                column: "TransactionCodeId",
                principalTable: "TransactionCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankAccounts_CurrencyCodes_CurrencyId",
                table: "BankAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_BankAccounts_MainDashboard_MainDashboardId",
                table: "BankAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_BankStatements_BankAccounts_BankAccountId",
                table: "BankStatements");

            migrationBuilder.DropForeignKey(
                name: "FK_BankStatements_StatementFiles_StatementFileId",
                table: "BankStatements");

            migrationBuilder.DropForeignKey(
                name: "FK_Histories_BankAccounts_BankAccountId",
                table: "Histories");

            migrationBuilder.DropForeignKey(
                name: "FK_StatementTransactions_BankStatements_BankStatementId",
                table: "StatementTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_StatementTransactions_TransactionCodes_TransactionCodeId",
                table: "StatementTransactions");

            migrationBuilder.DropTable(
                name: "MainDashboard");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TransactionCodes",
                table: "TransactionCodes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StatementTransactions",
                table: "StatementTransactions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StatementFiles",
                table: "StatementFiles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Histories",
                table: "Histories");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CurrencyCodes",
                table: "CurrencyCodes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_BankStatements",
                table: "BankStatements");

            migrationBuilder.DropPrimaryKey(
                name: "PK_BankAccounts",
                table: "BankAccounts");

            migrationBuilder.DropIndex(
                name: "IX_BankAccounts_MainDashboardId",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "MainDashboardId",
                table: "BankAccounts");

            migrationBuilder.RenameTable(
                name: "TransactionCodes",
                newName: "TransactionCode");

            migrationBuilder.RenameTable(
                name: "StatementTransactions",
                newName: "StatementTransaction");

            migrationBuilder.RenameTable(
                name: "StatementFiles",
                newName: "StatementFile");

            migrationBuilder.RenameTable(
                name: "Histories",
                newName: "History");

            migrationBuilder.RenameTable(
                name: "CurrencyCodes",
                newName: "Currency");

            migrationBuilder.RenameTable(
                name: "BankStatements",
                newName: "BankStatement");

            migrationBuilder.RenameTable(
                name: "BankAccounts",
                newName: "BankAccount");

            migrationBuilder.RenameIndex(
                name: "IX_TransactionCodes_Code",
                table: "TransactionCode",
                newName: "IX_TransactionCode_Code");

            migrationBuilder.RenameIndex(
                name: "IX_StatementTransactions_TransactionCodeId",
                table: "StatementTransaction",
                newName: "IX_StatementTransaction_TransactionCodeId");

            migrationBuilder.RenameIndex(
                name: "IX_StatementTransactions_BankStatementId",
                table: "StatementTransaction",
                newName: "IX_StatementTransaction_BankStatementId");

            migrationBuilder.RenameIndex(
                name: "IX_Histories_BankAccountId",
                table: "History",
                newName: "IX_History_BankAccountId");

            migrationBuilder.RenameIndex(
                name: "IX_BankStatements_StatementFileId",
                table: "BankStatement",
                newName: "IX_BankStatement_StatementFileId");

            migrationBuilder.RenameIndex(
                name: "IX_BankStatements_BankAccountId",
                table: "BankStatement",
                newName: "IX_BankStatement_BankAccountId");

            migrationBuilder.RenameIndex(
                name: "IX_BankAccounts_CurrencyId",
                table: "BankAccount",
                newName: "IX_BankAccount_CurrencyId");

            migrationBuilder.RenameIndex(
                name: "IX_BankAccounts_AccountNumber",
                table: "BankAccount",
                newName: "IX_BankAccount_AccountNumber");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TransactionCode",
                table: "TransactionCode",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StatementTransaction",
                table: "StatementTransaction",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StatementFile",
                table: "StatementFile",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_History",
                table: "History",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Currency",
                table: "Currency",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_BankStatement",
                table: "BankStatement",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_BankAccount",
                table: "BankAccount",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BankAccount_Currency_CurrencyId",
                table: "BankAccount",
                column: "CurrencyId",
                principalTable: "Currency",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BankStatement_BankAccount_BankAccountId",
                table: "BankStatement",
                column: "BankAccountId",
                principalTable: "BankAccount",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BankStatement_StatementFile_StatementFileId",
                table: "BankStatement",
                column: "StatementFileId",
                principalTable: "StatementFile",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_History_BankAccount_BankAccountId",
                table: "History",
                column: "BankAccountId",
                principalTable: "BankAccount",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StatementTransaction_BankStatement_BankStatementId",
                table: "StatementTransaction",
                column: "BankStatementId",
                principalTable: "BankStatement",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StatementTransaction_TransactionCode_TransactionCodeId",
                table: "StatementTransaction",
                column: "TransactionCodeId",
                principalTable: "TransactionCode",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
