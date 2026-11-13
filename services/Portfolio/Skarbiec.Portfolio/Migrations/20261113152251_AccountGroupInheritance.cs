using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AccountGroupInheritance : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "DefaultGroupId",
            table: "Accounts",
            newName: "GroupId");

        migrationBuilder.AddColumn<bool>(
            name: "GroupIsException",
            table: "Assets",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "GroupRepublishPending",
            table: "Assets",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        // AssetClass 0 (Cash) and 9 (BankAccount) are an account's money asset; the other classes are its components.
        migrationBuilder.Sql(
            """
            UPDATE "Accounts" a SET "GroupId" = COALESCE(m."GroupId", a."GroupId")
            FROM "Assets" m
            WHERE m."AccountId" = a."Id" AND m."AssetClass" IN (0, 9);
            """);

        migrationBuilder.Sql(
            """
            UPDATE "Assets" m SET "GroupRepublishPending" = true, "GroupId" = a."GroupId"
            FROM "Accounts" a
            WHERE m."AccountId" = a."Id" AND m."AssetClass" IN (0, 9) AND m."GroupId" IS DISTINCT FROM a."GroupId";
            """);

        migrationBuilder.Sql(
            """
            UPDATE "Assets" c SET "GroupIsException" = true
            FROM "Accounts" a
            WHERE c."AccountId" = a."Id" AND c."AssetClass" NOT IN (0, 9)
              AND c."GroupId" IS NOT NULL AND c."GroupId" IS DISTINCT FROM a."GroupId";
            """);

        migrationBuilder.Sql(
            """
            UPDATE "Assets" c SET "GroupRepublishPending" = true, "GroupId" = a."GroupId"
            FROM "Accounts" a
            WHERE c."AccountId" = a."Id" AND c."AssetClass" NOT IN (0, 9)
              AND NOT c."GroupIsException" AND c."GroupId" IS DISTINCT FROM a."GroupId";
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "GroupIsException",
            table: "Assets");

        migrationBuilder.DropColumn(
            name: "GroupRepublishPending",
            table: "Assets");

        migrationBuilder.RenameColumn(
            name: "GroupId",
            table: "Accounts",
            newName: "DefaultGroupId");
    }
}
