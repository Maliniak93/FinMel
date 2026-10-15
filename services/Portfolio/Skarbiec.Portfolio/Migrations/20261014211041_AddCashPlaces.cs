using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddCashPlaces : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CashPlaceId",
            table: "Assets",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "CashPlaces",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                IsArchived = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CashPlaces", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Assets_CashPlaceId_Currency",
            table: "Assets",
            columns: new[] { "CashPlaceId", "Currency" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CashPlaces_UserId_Name",
            table: "CashPlaces",
            columns: new[] { "UserId", "Name" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CashPlaces");

        migrationBuilder.DropIndex(
            name: "IX_Assets_CashPlaceId_Currency",
            table: "Assets");

        migrationBuilder.DropColumn(
            name: "CashPlaceId",
            table: "Assets");
    }
}
