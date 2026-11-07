using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class GlobalContributionLimits : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ContributionLimits",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Wrapper = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Year = table.Column<int>(type: "integer", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ContributionLimits", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ContributionLimits_UserId_Wrapper_Year",
            table: "ContributionLimits",
            columns: new[] { "UserId", "Wrapper", "Year" },
            unique: true);

        migrationBuilder.Sql(
            """
            INSERT INTO "ContributionLimits" ("Id", "UserId", "Wrapper", "Year", "Amount")
            SELECT gen_random_uuid(), l."UserId", a."Wrapper", l."Year", MAX(l."Amount")
            FROM "AccountContributionLimits" l
            JOIN "Accounts" a ON a."Id" = l."AccountId" AND a."UserId" = l."UserId"
            WHERE a."Wrapper" IN ('Ike', 'Ikze')
            GROUP BY l."UserId", a."Wrapper", l."Year"
            """);

        migrationBuilder.DropTable(
            name: "AccountContributionLimits");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ContributionLimits");

        migrationBuilder.CreateTable(
            name: "AccountContributionLimits",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Year = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccountContributionLimits", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AccountContributionLimits_AccountId_Year",
            table: "AccountContributionLimits",
            columns: new[] { "AccountId", "Year" },
            unique: true);
    }
}
