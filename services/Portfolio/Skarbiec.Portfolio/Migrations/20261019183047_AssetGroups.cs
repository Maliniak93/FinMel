using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AssetGroups : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "GroupId",
            table: "Assets",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "AssetGroups",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                TargetPln = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssetGroups", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Assets_GroupId",
            table: "Assets",
            column: "GroupId");

        migrationBuilder.CreateIndex(
            name: "IX_AssetGroups_UserId_Color",
            table: "AssetGroups",
            columns: new[] { "UserId", "Color" },
            unique: true);

        migrationBuilder.Sql(
            "CREATE UNIQUE INDEX \"IX_AssetGroups_UserId_LowerName\" ON \"AssetGroups\" (\"UserId\", lower(\"Name\"));");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AssetGroups");

        migrationBuilder.DropIndex(
            name: "IX_Assets_GroupId",
            table: "Assets");

        migrationBuilder.DropColumn(
            name: "GroupId",
            table: "Assets");
    }
}
