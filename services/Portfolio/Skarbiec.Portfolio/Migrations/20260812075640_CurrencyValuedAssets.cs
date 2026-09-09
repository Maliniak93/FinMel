using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class CurrencyValuedAssets : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ValuationMode",
            table: "Assets",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        // Backfill (M1.4): before this migration only two modes existed, distinguished implicitly by
        // InstrumentId being set (Market) or not (Manual) — Market is the column's DEFAULT (0), so
        // only Manual rows (InstrumentId IS NULL) need an explicit UPDATE to 1. No currency-valued
        // rows can exist yet — this migration is what introduces the mode — so this backfill is
        // exhaustive; nothing predates it that could be miscategorized.
        migrationBuilder.Sql(
            """UPDATE "Assets" SET "ValuationMode" = 1 WHERE "InstrumentId" IS NULL;""");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ValuationMode",
            table: "Assets");
    }
}
