using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class PositionEvents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AssetCount",
            table: "Portfolios");

        migrationBuilder.DropColumn(
            name: "TransactionCount",
            table: "Assets");

        migrationBuilder.AddColumn<long>(
            name: "Version",
            table: "Assets",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Version",
            table: "Assets");

        migrationBuilder.AddColumn<int>(
            name: "AssetCount",
            table: "Portfolios",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "TransactionCount",
            table: "Assets",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }
}
