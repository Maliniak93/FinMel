using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Reporting.Migrations;

/// <inheritdoc />
public partial class ReportingPerUser : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM \"HistoryRebuildRequests\"");
        migrationBuilder.Sql("DELETE FROM \"ValuationSnapshots\"");

        migrationBuilder.DropIndex(
            name: "IX_ValuationSnapshots_PortfolioId_Date",
            table: "ValuationSnapshots");

        migrationBuilder.DropIndex(
            name: "IX_ValuationSnapshots_UserId_Date",
            table: "ValuationSnapshots");

        migrationBuilder.DropPrimaryKey(
            name: "PK_HistoryRebuildRequests",
            table: "HistoryRebuildRequests");

        migrationBuilder.DropColumn(
            name: "PortfolioId",
            table: "ValuationSnapshots");

        migrationBuilder.DropColumn(
            name: "PortfolioId",
            table: "HistoryRebuildRequests");

        migrationBuilder.AddPrimaryKey(
            name: "PK_HistoryRebuildRequests",
            table: "HistoryRebuildRequests",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_ValuationSnapshots_UserId_Date",
            table: "ValuationSnapshots",
            columns: new[] { "UserId", "Date" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ValuationSnapshots_UserId_Date",
            table: "ValuationSnapshots");

        migrationBuilder.DropPrimaryKey(
            name: "PK_HistoryRebuildRequests",
            table: "HistoryRebuildRequests");

        migrationBuilder.AddColumn<Guid>(
            name: "PortfolioId",
            table: "ValuationSnapshots",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.AddColumn<Guid>(
            name: "PortfolioId",
            table: "HistoryRebuildRequests",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.AddPrimaryKey(
            name: "PK_HistoryRebuildRequests",
            table: "HistoryRebuildRequests",
            column: "PortfolioId");

        migrationBuilder.CreateIndex(
            name: "IX_ValuationSnapshots_PortfolioId_Date",
            table: "ValuationSnapshots",
            columns: new[] { "PortfolioId", "Date" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ValuationSnapshots_UserId_Date",
            table: "ValuationSnapshots",
            columns: new[] { "UserId", "Date" });
    }
}
