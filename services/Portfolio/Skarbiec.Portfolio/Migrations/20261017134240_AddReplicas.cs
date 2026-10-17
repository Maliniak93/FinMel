using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddReplicas : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TargetAllocationReplicas",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                SavedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Groups = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TargetAllocationReplicas", x => x.UserId);
            });

        migrationBuilder.CreateTable(
            name: "ValuationReplicas",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                IsStale = table.Column<bool>(type: "boolean", nullable: false),
                TotalPln = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ByAssetClass = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ValuationReplicas", x => x.UserId);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TargetAllocationReplicas");

        migrationBuilder.DropTable(
            name: "ValuationReplicas");
    }
}
