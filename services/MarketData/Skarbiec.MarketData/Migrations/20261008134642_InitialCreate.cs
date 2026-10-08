using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Skarbiec.MarketData.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AssetInstrumentLinks",
            columns: table => new
            {
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                InstrumentId = table.Column<Guid>(type: "uuid", nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false),
                IsRemoved = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssetInstrumentLinks", x => x.AssetId);
            });

        migrationBuilder.CreateTable(
            name: "BondSeries",
            columns: table => new
            {
                Code = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                Isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                SaleStart = table.Column<DateOnly>(type: "date", nullable: false),
                SaleEnd = table.Column<DateOnly>(type: "date", nullable: false),
                IssuePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                SwapPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                MarginPercent = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BondSeries", x => x.Code);
            });

        migrationBuilder.CreateTable(
            name: "Currencies",
            columns: table => new
            {
                Code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                DecimalPlaces = table.Column<int>(type: "integer", nullable: false),
                DisplayOrder = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Currencies", x => x.Code);
            });

        migrationBuilder.CreateTable(
            name: "FxRates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Pair = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                Rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FxRates", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "InstrumentUsages",
            columns: table => new
            {
                InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                AssetCount = table.Column<int>(type: "integer", nullable: false),
                FirstUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InstrumentUsages", x => x.InstrumentId);
            });

        migrationBuilder.CreateTable(
            name: "Instruments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Ticker = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Source = table.Column<int>(type: "integer", nullable: false),
                QuoteCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                AssetClass = table.Column<int>(type: "integer", nullable: false),
                Exchange = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                VerificationStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Verified")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Instruments", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "MarketDataInboxState",
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
                table.PrimaryKey("PK_MarketDataInboxState", x => x.Id);
                table.UniqueConstraint("AK_MarketDataInboxState_MessageId_ConsumerId", x => new { x.MessageId, x.ConsumerId });
            });

        migrationBuilder.CreateTable(
            name: "MarketDataOutboxState",
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
                table.PrimaryKey("PK_MarketDataOutboxState", x => x.OutboxId);
            });

        migrationBuilder.CreateTable(
            name: "PriceQuotes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                Close = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PriceQuotes", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "SyncRuns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Prices"),
                StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                SyncedCount = table.Column<int>(type: "integer", nullable: false),
                NoDataCount = table.Column<int>(type: "integer", nullable: false),
                FailedCount = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SyncRuns", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "BondSeriesPeriodRates",
            columns: table => new
            {
                SeriesCode = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                PeriodIndex = table.Column<int>(type: "integer", nullable: false),
                RatePercent = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BondSeriesPeriodRates", x => new { x.SeriesCode, x.PeriodIndex });
                table.ForeignKey(
                    name: "FK_BondSeriesPeriodRates_BondSeries_SeriesCode",
                    column: x => x.SeriesCode,
                    principalTable: "BondSeries",
                    principalColumn: "Code",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MarketDataOutboxMessage",
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
                table.PrimaryKey("PK_MarketDataOutboxMessage", x => x.SequenceNumber);
                table.ForeignKey(
                    name: "FK_MarketDataOutboxMessage_MarketDataInboxState_InboxMessageId~",
                    columns: x => new { x.InboxMessageId, x.InboxConsumerId },
                    principalTable: "MarketDataInboxState",
                    principalColumns: new[] { "MessageId", "ConsumerId" });
                table.ForeignKey(
                    name: "FK_MarketDataOutboxMessage_MarketDataOutboxState_OutboxId",
                    column: x => x.OutboxId,
                    principalTable: "MarketDataOutboxState",
                    principalColumn: "OutboxId");
            });

        migrationBuilder.CreateIndex(
            name: "IX_AssetInstrumentLinks_InstrumentId",
            table: "AssetInstrumentLinks",
            column: "InstrumentId");

        migrationBuilder.CreateIndex(
            name: "IX_BondSeries_SaleStart_SaleEnd",
            table: "BondSeries",
            columns: new[] { "SaleStart", "SaleEnd" });

        migrationBuilder.CreateIndex(
            name: "IX_FxRates_Pair_Date",
            table: "FxRates",
            columns: new[] { "Pair", "Date" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Instruments_Source_Ticker",
            table: "Instruments",
            columns: new[] { "Source", "Ticker" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MarketDataInboxState_Delivered",
            table: "MarketDataInboxState",
            column: "Delivered");

        migrationBuilder.CreateIndex(
            name: "IX_MarketDataOutboxMessage_EnqueueTime",
            table: "MarketDataOutboxMessage",
            column: "EnqueueTime");

        migrationBuilder.CreateIndex(
            name: "IX_MarketDataOutboxMessage_ExpirationTime",
            table: "MarketDataOutboxMessage",
            column: "ExpirationTime");

        migrationBuilder.CreateIndex(
            name: "IX_MarketDataOutboxMessage_InboxMessageId_InboxConsumerId_Sequ~",
            table: "MarketDataOutboxMessage",
            columns: new[] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MarketDataOutboxMessage_OutboxId_SequenceNumber",
            table: "MarketDataOutboxMessage",
            columns: new[] { "OutboxId", "SequenceNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MarketDataOutboxState_Created",
            table: "MarketDataOutboxState",
            column: "Created");

        migrationBuilder.CreateIndex(
            name: "IX_PriceQuotes_InstrumentId_Date",
            table: "PriceQuotes",
            columns: new[] { "InstrumentId", "Date" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SyncRuns_Kind_StartedAt",
            table: "SyncRuns",
            columns: new[] { "Kind", "StartedAt" },
            descending: new[] { false, true });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AssetInstrumentLinks");

        migrationBuilder.DropTable(
            name: "BondSeriesPeriodRates");

        migrationBuilder.DropTable(
            name: "Currencies");

        migrationBuilder.DropTable(
            name: "FxRates");

        migrationBuilder.DropTable(
            name: "InstrumentUsages");

        migrationBuilder.DropTable(
            name: "Instruments");

        migrationBuilder.DropTable(
            name: "MarketDataOutboxMessage");

        migrationBuilder.DropTable(
            name: "PriceQuotes");

        migrationBuilder.DropTable(
            name: "SyncRuns");

        migrationBuilder.DropTable(
            name: "BondSeries");

        migrationBuilder.DropTable(
            name: "MarketDataInboxState");

        migrationBuilder.DropTable(
            name: "MarketDataOutboxState");
    }
}
