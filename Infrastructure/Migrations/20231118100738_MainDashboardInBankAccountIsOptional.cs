using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MainDashboardInBankAccountIsOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankAccounts_MainDashboard_MainDashboardId",
                table: "BankAccounts");

            migrationBuilder.AddForeignKey(
                name: "FK_BankAccounts_MainDashboard_MainDashboardId",
                table: "BankAccounts",
                column: "MainDashboardId",
                principalTable: "MainDashboard",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankAccounts_MainDashboard_MainDashboardId",
                table: "BankAccounts");

            migrationBuilder.AddForeignKey(
                name: "FK_BankAccounts_MainDashboard_MainDashboardId",
                table: "BankAccounts",
                column: "MainDashboardId",
                principalTable: "MainDashboard",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
