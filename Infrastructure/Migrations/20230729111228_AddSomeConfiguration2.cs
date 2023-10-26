using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSomeConfiguration2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankAccount_Currency_CurrencyId",
                table: "BankAccount");

            migrationBuilder.DropForeignKey(
                name: "FK_BankStatement_BankAccount_BankAccountId",
                table: "BankStatement");

            migrationBuilder.DropForeignKey(
                name: "FK_BankStatement_StatementFile_StatementFileId",
                table: "BankStatement");

            migrationBuilder.DropForeignKey(
                name: "FK_StatementTransaction_BankStatement_BankStatementId",
                table: "StatementTransaction");

            migrationBuilder.AlterColumn<string>(
                name: "DescriptionOptional",
                table: "StatementTransaction",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DescriptionBase",
                table: "StatementTransaction",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "BankStatementId",
                table: "StatementTransaction",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StatementNumber",
                table: "BankStatement",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "StatementFileId",
                table: "BankStatement",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "BankAccountId",
                table: "BankStatement",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CurrencyId",
                table: "BankAccount",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BankAccount_Currency_CurrencyId",
                table: "BankAccount",
                column: "CurrencyId",
                principalTable: "Currency",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BankStatement_BankAccount_BankAccountId",
                table: "BankStatement",
                column: "BankAccountId",
                principalTable: "BankAccount",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BankStatement_StatementFile_StatementFileId",
                table: "BankStatement",
                column: "StatementFileId",
                principalTable: "StatementFile",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StatementTransaction_BankStatement_BankStatementId",
                table: "StatementTransaction",
                column: "BankStatementId",
                principalTable: "BankStatement",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankAccount_Currency_CurrencyId",
                table: "BankAccount");

            migrationBuilder.DropForeignKey(
                name: "FK_BankStatement_BankAccount_BankAccountId",
                table: "BankStatement");

            migrationBuilder.DropForeignKey(
                name: "FK_BankStatement_StatementFile_StatementFileId",
                table: "BankStatement");

            migrationBuilder.DropForeignKey(
                name: "FK_StatementTransaction_BankStatement_BankStatementId",
                table: "StatementTransaction");

            migrationBuilder.AlterColumn<string>(
                name: "DescriptionOptional",
                table: "StatementTransaction",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DescriptionBase",
                table: "StatementTransaction",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "BankStatementId",
                table: "StatementTransaction",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "StatementNumber",
                table: "BankStatement",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<int>(
                name: "StatementFileId",
                table: "BankStatement",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "BankAccountId",
                table: "BankStatement",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "CurrencyId",
                table: "BankAccount",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_BankAccount_Currency_CurrencyId",
                table: "BankAccount",
                column: "CurrencyId",
                principalTable: "Currency",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BankStatement_BankAccount_BankAccountId",
                table: "BankStatement",
                column: "BankAccountId",
                principalTable: "BankAccount",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BankStatement_StatementFile_StatementFileId",
                table: "BankStatement",
                column: "StatementFileId",
                principalTable: "StatementFile",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StatementTransaction_BankStatement_BankStatementId",
                table: "StatementTransaction",
                column: "BankStatementId",
                principalTable: "BankStatement",
                principalColumn: "Id");
        }
    }
}
