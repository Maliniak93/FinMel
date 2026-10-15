using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddRecurringIncomes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RecurringIncomes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                EndDate = table.Column<DateOnly>(type: "date", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RecurringIncomes", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "RecurringIncomeVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                RecurringIncomeId = table.Column<Guid>(type: "uuid", nullable: false),
                EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                IntervalMonths = table.Column<int>(type: "integer", nullable: false),
                DayKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                DayOfMonth = table.Column<int>(type: "integer", nullable: true),
                Shift = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RecurringIncomeVersions", x => x.Id);
                table.ForeignKey(
                    name: "FK_RecurringIncomeVersions_Accounts_AccountId",
                    column: x => x.AccountId,
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_RecurringIncomeVersions_RecurringIncomes_RecurringIncomeId",
                    column: x => x.RecurringIncomeId,
                    principalTable: "RecurringIncomes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RecurringIncomeVersions_AccountId",
            table: "RecurringIncomeVersions",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_RecurringIncomeVersions_RecurringIncomeId",
            table: "RecurringIncomeVersions",
            column: "RecurringIncomeId");

        migrationBuilder.CreateIndex(
            name: "IX_RecurringIncomes_UserId_Name",
            table: "RecurringIncomes",
            columns: new[] { "UserId", "Name" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RecurringIncomeVersions");

        migrationBuilder.DropTable(
            name: "RecurringIncomes");
    }
}
