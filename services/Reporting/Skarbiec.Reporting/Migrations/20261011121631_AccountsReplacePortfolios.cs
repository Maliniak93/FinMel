using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Reporting.Migrations;

/// <inheritdoc />
public partial class AccountsReplacePortfolios : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Positions_PortfolioId",
            table: "Positions");

        migrationBuilder.DropColumn(
            name: "PortfolioArchivedOn",
            table: "Positions");

        migrationBuilder.DropColumn(
            name: "PortfolioId",
            table: "Positions");

        migrationBuilder.DropColumn(
            name: "PortfolioIsArchived",
            table: "Positions");

        migrationBuilder.DropColumn(
            name: "PortfolioId",
            table: "AssetValuations");

        migrationBuilder.AddColumn<Guid>(
            name: "AccountId",
            table: "Positions",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "AccountId",
            table: "AssetValuations",
            type: "uuid",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AccountId",
            table: "Positions");

        migrationBuilder.DropColumn(
            name: "AccountId",
            table: "AssetValuations");

        migrationBuilder.AddColumn<DateOnly>(
            name: "PortfolioArchivedOn",
            table: "Positions",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "PortfolioId",
            table: "Positions",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.AddColumn<bool>(
            name: "PortfolioIsArchived",
            table: "Positions",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<Guid>(
            name: "PortfolioId",
            table: "AssetValuations",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.CreateIndex(
            name: "IX_Positions_PortfolioId",
            table: "Positions",
            column: "PortfolioId");
    }
}
