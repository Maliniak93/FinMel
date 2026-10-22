using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddGroupContributionAndAllocation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ClassAllocation",
            table: "AssetGroups",
            type: "jsonb",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddColumn<string>(
            name: "Kind",
            table: "AssetGroups",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Regular");

        migrationBuilder.AddColumn<decimal>(
            name: "MonthlyContributionPln",
            table: "AssetGroups",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ClassAllocation",
            table: "AssetGroups");

        migrationBuilder.DropColumn(
            name: "Kind",
            table: "AssetGroups");

        migrationBuilder.DropColumn(
            name: "MonthlyContributionPln",
            table: "AssetGroups");
    }
}
