using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddDepositSettlement : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "SettledGrossInterest",
            table: "TermDeposits",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "SettledOn",
            table: "TermDeposits",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "SettledTax",
            table: "TermDeposits",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SettledGrossInterest",
            table: "TermDeposits");

        migrationBuilder.DropColumn(
            name: "SettledOn",
            table: "TermDeposits");

        migrationBuilder.DropColumn(
            name: "SettledTax",
            table: "TermDeposits");
    }
}
