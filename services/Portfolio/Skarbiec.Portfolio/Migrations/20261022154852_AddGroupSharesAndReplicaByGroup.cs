using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddGroupSharesAndReplicaByGroup : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ByGroup",
            table: "ValuationReplicas",
            type: "jsonb",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddColumn<decimal>(
            name: "SharePercent",
            table: "AssetGroups",
            type: "numeric(4,1)",
            precision: 4,
            scale: 1,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ByGroup",
            table: "ValuationReplicas");

        migrationBuilder.DropColumn(
            name: "SharePercent",
            table: "AssetGroups");
    }
}
