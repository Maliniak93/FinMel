using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Skarbiec.Reporting.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AssetValuations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                AssetClass = table.Column<int>(type: "integer", nullable: false),
                Quantity = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                PriceUsed = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                PriceDate = table.Column<DateOnly>(type: "date", nullable: true),
                FxRateUsed = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                ValuePln = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                IsStale = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssetValuations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Positions",
            columns: table => new
            {
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                AssetClass = table.Column<int>(type: "integer", nullable: false),
                ValuationMode = table.Column<int>(type: "integer", nullable: false),
                InstrumentId = table.Column<Guid>(type: "uuid", nullable: true),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Quantity = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                ManualValueAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                ManualValueDate = table.Column<DateOnly>(type: "date", nullable: true),
                PortfolioIsArchived = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Positions", x => x.AssetId);
            });

        migrationBuilder.CreateTable(
            name: "ReportingInboxState",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                LockId = table.Column<Guid>(type: "uuid", nullable: false),
                RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                Received = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReceiveCount = table.Column<int>(type: "integer", nullable: false),
                ExpirationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Consumed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReportingInboxState", x => x.Id);
                table.UniqueConstraint("AK_ReportingInboxState_MessageId_ConsumerId", x => new { x.MessageId, x.ConsumerId });
            });

        migrationBuilder.CreateTable(
            name: "ReportingOutboxState",
            columns: table => new
            {
                OutboxId = table.Column<Guid>(type: "uuid", nullable: false),
                LockId = table.Column<Guid>(type: "uuid", nullable: false),
                RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReportingOutboxState", x => x.OutboxId);
            });

        migrationBuilder.CreateTable(
            name: "ValuationSnapshots",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                TotalPln = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                IsStale = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ValuationSnapshots", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ReportingOutboxMessage",
            columns: table => new
            {
                SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                EnqueueTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SentTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Headers = table.Column<string>(type: "text", nullable: true),
                Properties = table.Column<string>(type: "text", nullable: true),
                InboxMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                InboxConsumerId = table.Column<Guid>(type: "uuid", nullable: true),
                OutboxId = table.Column<Guid>(type: "uuid", nullable: true),
                MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                ContentType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                MessageType = table.Column<string>(type: "text", nullable: false),
                Body = table.Column<string>(type: "text", nullable: false),
                ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                InitiatorId = table.Column<Guid>(type: "uuid", nullable: true),
                RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                SourceAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                DestinationAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                ResponseAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                FaultAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                ExpirationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReportingOutboxMessage", x => x.SequenceNumber);
                table.ForeignKey(
                    name: "FK_ReportingOutboxMessage_ReportingInboxState_InboxMessageId_I~",
                    columns: x => new { x.InboxMessageId, x.InboxConsumerId },
                    principalTable: "ReportingInboxState",
                    principalColumns: new[] { "MessageId", "ConsumerId" });
                table.ForeignKey(
                    name: "FK_ReportingOutboxMessage_ReportingOutboxState_OutboxId",
                    column: x => x.OutboxId,
                    principalTable: "ReportingOutboxState",
                    principalColumn: "OutboxId");
            });

        migrationBuilder.CreateIndex(
            name: "IX_AssetValuations_AssetId_Date",
            table: "AssetValuations",
            columns: new[] { "AssetId", "Date" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AssetValuations_UserId_Date",
            table: "AssetValuations",
            columns: new[] { "UserId", "Date" });

        migrationBuilder.CreateIndex(
            name: "IX_Positions_PortfolioId",
            table: "Positions",
            column: "PortfolioId");

        migrationBuilder.CreateIndex(
            name: "IX_ReportingInboxState_Delivered",
            table: "ReportingInboxState",
            column: "Delivered");

        migrationBuilder.CreateIndex(
            name: "IX_ReportingOutboxMessage_EnqueueTime",
            table: "ReportingOutboxMessage",
            column: "EnqueueTime");

        migrationBuilder.CreateIndex(
            name: "IX_ReportingOutboxMessage_ExpirationTime",
            table: "ReportingOutboxMessage",
            column: "ExpirationTime");

        migrationBuilder.CreateIndex(
            name: "IX_ReportingOutboxMessage_InboxMessageId_InboxConsumerId_Seque~",
            table: "ReportingOutboxMessage",
            columns: new[] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ReportingOutboxMessage_OutboxId_SequenceNumber",
            table: "ReportingOutboxMessage",
            columns: new[] { "OutboxId", "SequenceNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ReportingOutboxState_Created",
            table: "ReportingOutboxState",
            column: "Created");

        migrationBuilder.CreateIndex(
            name: "IX_ValuationSnapshots_PortfolioId_Date",
            table: "ValuationSnapshots",
            columns: new[] { "PortfolioId", "Date" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ValuationSnapshots_UserId_Date",
            table: "ValuationSnapshots",
            columns: new[] { "UserId", "Date" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AssetValuations");

        migrationBuilder.DropTable(
            name: "Positions");

        migrationBuilder.DropTable(
            name: "ReportingOutboxMessage");

        migrationBuilder.DropTable(
            name: "ValuationSnapshots");

        migrationBuilder.DropTable(
            name: "ReportingInboxState");

        migrationBuilder.DropTable(
            name: "ReportingOutboxState");
    }
}
