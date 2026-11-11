using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class GroupContributions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Contributions",
            table: "AssetGroups",
            type: "jsonb",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.Sql(
            """
            UPDATE "AssetGroups"
            SET "Contributions" = jsonb_build_array(jsonb_build_object('AmountPln', "MonthlyContributionPln", 'AccountId', NULL))
            WHERE "MonthlyContributionPln" > 0
            """);

        migrationBuilder.DropColumn(
            name: "MonthlyContributionPln",
            table: "AssetGroups");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "MonthlyContributionPln",
            table: "AssetGroups",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.Sql(
            """
            UPDATE "AssetGroups"
            SET "MonthlyContributionPln" = COALESCE((SELECT SUM((c ->> 'AmountPln')::numeric) FROM jsonb_array_elements("Contributions") AS c), 0)
            """);

        migrationBuilder.DropColumn(
            name: "Contributions",
            table: "AssetGroups");
    }
}
