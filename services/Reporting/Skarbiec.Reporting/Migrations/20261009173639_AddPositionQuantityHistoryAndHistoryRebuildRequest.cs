using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Reporting.Migrations;

/// <inheritdoc />
public partial class AddPositionQuantityHistoryAndHistoryRebuildRequest : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "ArchivedOn",
            table: "Positions",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "PortfolioArchivedOn",
            table: "Positions",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "QuantityHistory",
            table: "Positions",
            type: "jsonb",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.CreateTable(
            name: "HistoryRebuildRequests",
            columns: table => new
            {
                PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HistoryRebuildRequests", x => x.PortfolioId);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "HistoryRebuildRequests");

        migrationBuilder.DropColumn(
            name: "ArchivedOn",
            table: "Positions");

        migrationBuilder.DropColumn(
            name: "PortfolioArchivedOn",
            table: "Positions");

        migrationBuilder.DropColumn(
            name: "QuantityHistory",
            table: "Positions");
    }
}
