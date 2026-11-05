using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.MarketData.Migrations;

/// <inheritdoc />
public partial class AddSyncRunSource : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Source",
            table: "SyncRuns",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Schedule");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Source",
            table: "SyncRuns");
    }
}
