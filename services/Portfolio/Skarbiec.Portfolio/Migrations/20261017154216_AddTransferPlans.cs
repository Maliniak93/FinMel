using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.Portfolio.Migrations;

/// <inheritdoc />
public partial class AddTransferPlans : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TransferPlans",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Month = table.Column<DateOnly>(type: "date", nullable: false),
                IsSkipped = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TransferPlans", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "TransferPlanBatches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                Number = table.Column<int>(type: "integer", nullable: false),
                AcceptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ValuationDate = table.Column<DateOnly>(type: "date", nullable: true),
                MinimumTransferPln = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TransferPlanBatches", x => x.Id);
                table.ForeignKey(
                    name: "FK_TransferPlanBatches_TransferPlans_PlanId",
                    column: x => x.PlanId,
                    principalTable: "TransferPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TransferPlanRows",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                BucketId = table.Column<Guid>(type: "uuid", nullable: false),
                BucketName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                AccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                GroupName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                AssetClasses = table.Column<int[]>(type: "integer[]", nullable: false),
                AmountPln = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Marker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Position = table.Column<int>(type: "integer", nullable: false),
                CheckedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TransferPlanRows", x => x.Id);
                table.ForeignKey(
                    name: "FK_TransferPlanRows_TransferPlanBatches_BatchId",
                    column: x => x.BatchId,
                    principalTable: "TransferPlanBatches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TransferPlanBatches_PlanId_Number",
            table: "TransferPlanBatches",
            columns: new[] { "PlanId", "Number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TransferPlanRows_BatchId",
            table: "TransferPlanRows",
            column: "BatchId");

        migrationBuilder.CreateIndex(
            name: "IX_TransferPlans_UserId_Month",
            table: "TransferPlans",
            columns: new[] { "UserId", "Month" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TransferPlanRows");

        migrationBuilder.DropTable(
            name: "TransferPlanBatches");

        migrationBuilder.DropTable(
            name: "TransferPlans");
    }
}
