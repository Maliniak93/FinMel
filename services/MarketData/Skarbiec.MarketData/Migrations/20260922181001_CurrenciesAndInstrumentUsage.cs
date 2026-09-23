using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.MarketData.Migrations;

/// <inheritdoc />
public partial class CurrenciesAndInstrumentUsage : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Kind",
            table: "SyncRuns",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Prices");

        migrationBuilder.CreateTable(
            name: "AssetInstrumentLinks",
            columns: table => new
            {
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                InstrumentId = table.Column<Guid>(type: "uuid", nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false),
                IsRemoved = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssetInstrumentLinks", x => x.AssetId);
            });

        migrationBuilder.CreateTable(
            name: "Currencies",
            columns: table => new
            {
                Code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                DecimalPlaces = table.Column<int>(type: "integer", nullable: false),
                DisplayOrder = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Currencies", x => x.Code);
            });

        migrationBuilder.CreateTable(
            name: "InstrumentUsages",
            columns: table => new
            {
                InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                AssetCount = table.Column<int>(type: "integer", nullable: false),
                FirstUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InstrumentUsages", x => x.InstrumentId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SyncRuns_Kind_StartedAt",
            table: "SyncRuns",
            columns: new[] { "Kind", "StartedAt" },
            descending: new[] { false, true });

        migrationBuilder.CreateIndex(
            name: "IX_AssetInstrumentLinks_InstrumentId",
            table: "AssetInstrumentLinks",
            column: "InstrumentId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AssetInstrumentLinks");

        migrationBuilder.DropTable(
            name: "Currencies");

        migrationBuilder.DropTable(
            name: "InstrumentUsages");

        migrationBuilder.DropIndex(
            name: "IX_SyncRuns_Kind_StartedAt",
            table: "SyncRuns");

        migrationBuilder.DropColumn(
            name: "Kind",
            table: "SyncRuns");
    }
}
