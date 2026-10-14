using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class DepositsFromBankAccount : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TaxExempt",
            table: "TermDeposits");

        migrationBuilder.AddColumn<Guid>(
            name: "ReturnAccountId",
            table: "TermDeposits",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.AddColumn<Guid>(
            name: "SourceAccountId",
            table: "TermDeposits",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ReturnAccountId",
            table: "TermDeposits");

        migrationBuilder.DropColumn(
            name: "SourceAccountId",
            table: "TermDeposits");

        migrationBuilder.AddColumn<bool>(
            name: "TaxExempt",
            table: "TermDeposits",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }
}
