using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Reporting.Migrations;

/// <inheritdoc />
public partial class AddValuationSnapshotUserIdDateIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_ValuationSnapshots_UserId_Date",
            table: "ValuationSnapshots",
            columns: new[] { "UserId", "Date" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ValuationSnapshots_UserId_Date",
            table: "ValuationSnapshots");
    }
}
