using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddPaymentLeadDays : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(
            name: "PK_TransferPlanSettings",
            table: "TransferPlanSettings");

        migrationBuilder.RenameTable(
            name: "TransferPlanSettings",
            newName: "PlanningSettings");

        migrationBuilder.AddPrimaryKey(
            name: "PK_PlanningSettings",
            table: "PlanningSettings",
            column: "UserId");

        migrationBuilder.AddColumn<int>(
            name: "PaymentLeadDays",
            table: "PlanningSettings",
            type: "integer",
            nullable: false,
            defaultValue: 7);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PaymentLeadDays",
            table: "PlanningSettings");

        migrationBuilder.DropPrimaryKey(
            name: "PK_PlanningSettings",
            table: "PlanningSettings");

        migrationBuilder.RenameTable(
            name: "PlanningSettings",
            newName: "TransferPlanSettings");

        migrationBuilder.AddPrimaryKey(
            name: "PK_TransferPlanSettings",
            table: "TransferPlanSettings",
            column: "UserId");
    }
}
