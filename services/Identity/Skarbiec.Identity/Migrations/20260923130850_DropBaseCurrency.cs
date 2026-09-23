using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Identity.Migrations;

/// <inheritdoc />
public partial class DropBaseCurrency : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "BaseCurrency",
            table: "AspNetUsers");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BaseCurrency",
            table: "AspNetUsers",
            type: "character varying(3)",
            maxLength: 3,
            nullable: false,
            defaultValue: "PLN");
    }
}
