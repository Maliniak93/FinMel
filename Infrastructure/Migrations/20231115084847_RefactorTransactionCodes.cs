using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefactorTransactionCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsExpenseIncome",
                table: "TransactionCodes");

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "TransactionCodes",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Type",
                table: "TransactionCodes");

            migrationBuilder.AddColumn<bool>(
                name: "IsExpenseIncome",
                table: "TransactionCodes",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
