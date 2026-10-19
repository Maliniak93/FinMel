using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class TransactionEntryOrder : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "transaction_entry_order");

        migrationBuilder.AddColumn<long>(
            name: "EntryOrder",
            table: "Transactions",
            type: "bigint",
            nullable: false,
            defaultValueSql: "nextval('transaction_entry_order')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "EntryOrder",
            table: "Transactions");

        migrationBuilder.DropSequence(
            name: "transaction_entry_order");
    }
}
