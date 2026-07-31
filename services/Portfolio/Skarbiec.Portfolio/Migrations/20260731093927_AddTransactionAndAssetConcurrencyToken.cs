using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddTransactionAndAssetConcurrencyToken : Migration
{
    // xmin is Postgres's own system column — every row already has one, so there is nothing
    // to add or drop. The scaffolder doesn't know that "xmin" is special and generates
    // AddColumn/DropColumn by default, which fails against Postgres ("column name "xmin"
    // conflicts with a system column name"). Both bodies are intentionally empty; this
    // migration exists only so the model snapshot picks up the new concurrency-token mapping.

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
