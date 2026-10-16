using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddRecurringExpenses : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RecurringExpenses",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                CreatedOn = table.Column<DateOnly>(type: "date", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RecurringExpenses", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "RecurringExpenseVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                RecurringExpenseId = table.Column<Guid>(type: "uuid", nullable: false),
                EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                IntervalMonths = table.Column<int>(type: "integer", nullable: false),
                DayKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                DayOfMonth = table.Column<int>(type: "integer", nullable: true),
                Shift = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RecurringExpenseVersions", x => x.Id);
                table.ForeignKey(
                    name: "FK_RecurringExpenseVersions_RecurringExpenses_RecurringExpense~",
                    column: x => x.RecurringExpenseId,
                    principalTable: "RecurringExpenses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RecurringExpenseVersions_RecurringExpenseId",
            table: "RecurringExpenseVersions",
            column: "RecurringExpenseId");

        migrationBuilder.CreateIndex(
            name: "IX_RecurringExpenses_UserId_Name",
            table: "RecurringExpenses",
            columns: new[] { "UserId", "Name" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RecurringExpenseVersions");

        migrationBuilder.DropTable(
            name: "RecurringExpenses");
    }
}
