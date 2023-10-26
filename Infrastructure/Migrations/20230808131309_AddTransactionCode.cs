using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsExpenseIncome",
                table: "StatementTransaction",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TransactionCodeId",
                table: "StatementTransaction",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TransactionCode",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsExpenseIncome = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionCode", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StatementTransaction_TransactionCodeId",
                table: "StatementTransaction",
                column: "TransactionCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatement_StatementNumber",
                table: "BankStatement",
                column: "StatementNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionCode_Code",
                table: "TransactionCode",
                column: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_StatementTransaction_TransactionCode_TransactionCodeId",
                table: "StatementTransaction",
                column: "TransactionCodeId",
                principalTable: "TransactionCode",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StatementTransaction_TransactionCode_TransactionCodeId",
                table: "StatementTransaction");

            migrationBuilder.DropTable(
                name: "TransactionCode");

            migrationBuilder.DropIndex(
                name: "IX_StatementTransaction_TransactionCodeId",
                table: "StatementTransaction");

            migrationBuilder.DropIndex(
                name: "IX_BankStatement_StatementNumber",
                table: "BankStatement");

            migrationBuilder.DropColumn(
                name: "IsExpenseIncome",
                table: "StatementTransaction");

            migrationBuilder.DropColumn(
                name: "TransactionCodeId",
                table: "StatementTransaction");
        }
    }
}
