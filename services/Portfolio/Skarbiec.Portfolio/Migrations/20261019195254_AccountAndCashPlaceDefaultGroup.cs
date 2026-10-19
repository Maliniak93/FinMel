using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AccountAndCashPlaceDefaultGroup : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "DefaultGroupId",
            table: "CashPlaces",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "DefaultGroupId",
            table: "Accounts",
            type: "uuid",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DefaultGroupId",
            table: "CashPlaces");

        migrationBuilder.DropColumn(
            name: "DefaultGroupId",
            table: "Accounts");
    }
}
