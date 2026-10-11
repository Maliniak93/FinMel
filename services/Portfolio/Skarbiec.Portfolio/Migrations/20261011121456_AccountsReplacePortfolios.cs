using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AccountsReplacePortfolios : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Portfolios");

        migrationBuilder.DropIndex(
            name: "IX_Assets_PortfolioId",
            table: "Assets");

        migrationBuilder.DropColumn(
            name: "TaxExempt",
            table: "TreasuryBonds");

        migrationBuilder.DropColumn(
            name: "PortfolioId",
            table: "Assets");

        migrationBuilder.AddColumn<Guid>(
            name: "AccountId",
            table: "Assets",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Accounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Wrapper = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                IsArchived = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Accounts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Assets_AccountId",
            table: "Assets",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_Accounts_UserId_Name",
            table: "Accounts",
            columns: new[] { "UserId", "Name" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Accounts");

        migrationBuilder.DropIndex(
            name: "IX_Assets_AccountId",
            table: "Assets");

        migrationBuilder.DropColumn(
            name: "AccountId",
            table: "Assets");

        migrationBuilder.AddColumn<bool>(
            name: "TaxExempt",
            table: "TreasuryBonds",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<Guid>(
            name: "PortfolioId",
            table: "Assets",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.CreateTable(
            name: "Portfolios",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Portfolios", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Assets_PortfolioId",
            table: "Assets",
            column: "PortfolioId");

        migrationBuilder.CreateIndex(
            name: "IX_Portfolios_UserId_Name",
            table: "Portfolios",
            columns: new[] { "UserId", "Name" },
            unique: true);
    }
}
