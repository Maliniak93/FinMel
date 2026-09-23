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
        migrationBuilder.EnsureSchema(
            name: "quartz");

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
            name: "qrtz_calendars",
            schema: "quartz",
            columns: table => new
            {
                sched_name = table.Column<string>(type: "text", nullable: false),
                calendar_name = table.Column<string>(type: "text", nullable: false),
                calendar = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qrtz_calendars", x => new { x.sched_name, x.calendar_name });
            });

        migrationBuilder.CreateTable(
            name: "qrtz_fired_triggers",
            schema: "quartz",
            columns: table => new
            {
                sched_name = table.Column<string>(type: "text", nullable: false),
                entry_id = table.Column<string>(type: "text", nullable: false),
                trigger_name = table.Column<string>(type: "text", nullable: false),
                trigger_group = table.Column<string>(type: "text", nullable: false),
                instance_name = table.Column<string>(type: "text", nullable: false),
                fired_time = table.Column<long>(type: "bigint", nullable: false),
                sched_time = table.Column<long>(type: "bigint", nullable: false),
                priority = table.Column<int>(type: "integer", nullable: false),
                state = table.Column<string>(type: "text", nullable: false),
                job_name = table.Column<string>(type: "text", nullable: true),
                job_group = table.Column<string>(type: "text", nullable: true),
                is_nonconcurrent = table.Column<bool>(type: "bool", nullable: false),
                requests_recovery = table.Column<bool>(type: "bool", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qrtz_fired_triggers", x => new { x.sched_name, x.entry_id });
            });

        migrationBuilder.CreateTable(
            name: "qrtz_job_details",
            schema: "quartz",
            columns: table => new
            {
                sched_name = table.Column<string>(type: "text", nullable: false),
                job_name = table.Column<string>(type: "text", nullable: false),
                job_group = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: true),
                job_class_name = table.Column<string>(type: "text", nullable: false),
                is_durable = table.Column<bool>(type: "bool", nullable: false),
                is_nonconcurrent = table.Column<bool>(type: "bool", nullable: false),
                is_update_data = table.Column<bool>(type: "bool", nullable: false),
                requests_recovery = table.Column<bool>(type: "bool", nullable: false),
                job_data = table.Column<byte[]>(type: "bytea", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qrtz_job_details", x => new { x.sched_name, x.job_name, x.job_group });
            });

        migrationBuilder.CreateTable(
            name: "qrtz_locks",
            schema: "quartz",
            columns: table => new
            {
                sched_name = table.Column<string>(type: "text", nullable: false),
                lock_name = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qrtz_locks", x => new { x.sched_name, x.lock_name });
            });

        migrationBuilder.CreateTable(
            name: "qrtz_paused_trigger_grps",
            schema: "quartz",
            columns: table => new
            {
                sched_name = table.Column<string>(type: "text", nullable: false),
                trigger_group = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qrtz_paused_trigger_grps", x => new { x.sched_name, x.trigger_group });
            });

        migrationBuilder.CreateTable(
            name: "qrtz_scheduler_state",
            schema: "quartz",
            columns: table => new
            {
                sched_name = table.Column<string>(type: "text", nullable: false),
                instance_name = table.Column<string>(type: "text", nullable: false),
                last_checkin_time = table.Column<long>(type: "bigint", nullable: false),
                checkin_interval = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qrtz_scheduler_state", x => new { x.sched_name, x.instance_name });
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

        migrationBuilder.CreateTable(
            name: "qrtz_triggers",
            schema: "quartz",
            columns: table => new
            {
                sched_name = table.Column<string>(type: "text", nullable: false),
                trigger_name = table.Column<string>(type: "text", nullable: false),
                trigger_group = table.Column<string>(type: "text", nullable: false),
                job_name = table.Column<string>(type: "text", nullable: false),
                job_group = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: true),
                next_fire_time = table.Column<long>(type: "bigint", nullable: true),
                prev_fire_time = table.Column<long>(type: "bigint", nullable: true),
                priority = table.Column<int>(type: "integer", nullable: true),
                trigger_state = table.Column<string>(type: "text", nullable: false),
                trigger_type = table.Column<string>(type: "text", nullable: false),
                start_time = table.Column<long>(type: "bigint", nullable: false),
                end_time = table.Column<long>(type: "bigint", nullable: true),
                misfire_orig_fire_time = table.Column<long>(type: "bigint", nullable: true),
                calendar_name = table.Column<string>(type: "text", nullable: true),
                misfire_instr = table.Column<short>(type: "smallint", nullable: true),
                job_data = table.Column<byte[]>(type: "bytea", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qrtz_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                table.ForeignKey(
                    name: "FK_qrtz_triggers_qrtz_job_details_sched_name_job_name_job_group",
                    columns: x => new { x.sched_name, x.job_name, x.job_group },
                    principalSchema: "quartz",
                    principalTable: "qrtz_job_details",
                    principalColumns: new[] { "sched_name", "job_name", "job_group" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "qrtz_blob_triggers",
            schema: "quartz",
            columns: table => new
            {
                sched_name = table.Column<string>(type: "text", nullable: false),
                trigger_name = table.Column<string>(type: "text", nullable: false),
                trigger_group = table.Column<string>(type: "text", nullable: false),
                blob_data = table.Column<byte[]>(type: "bytea", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qrtz_blob_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                table.ForeignKey(
                    name: "FK_qrtz_blob_triggers_qrtz_triggers_sched_name_trigger_name_tr~",
                    columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                    principalSchema: "quartz",
                    principalTable: "qrtz_triggers",
                    principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "qrtz_cron_triggers",
            schema: "quartz",
            columns: table => new
            {
                sched_name = table.Column<string>(type: "text", nullable: false),
                trigger_name = table.Column<string>(type: "text", nullable: false),
                trigger_group = table.Column<string>(type: "text", nullable: false),
                cron_expression = table.Column<string>(type: "text", nullable: false),
                time_zone_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qrtz_cron_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                table.ForeignKey(
                    name: "FK_qrtz_cron_triggers_qrtz_triggers_sched_name_trigger_name_tr~",
                    columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                    principalSchema: "quartz",
                    principalTable: "qrtz_triggers",
                    principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "qrtz_simple_triggers",
            schema: "quartz",
            columns: table => new
            {
                sched_name = table.Column<string>(type: "text", nullable: false),
                trigger_name = table.Column<string>(type: "text", nullable: false),
                trigger_group = table.Column<string>(type: "text", nullable: false),
                repeat_count = table.Column<long>(type: "bigint", nullable: false),
                repeat_interval = table.Column<long>(type: "bigint", nullable: false),
                times_triggered = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qrtz_simple_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                table.ForeignKey(
                    name: "FK_qrtz_simple_triggers_qrtz_triggers_sched_name_trigger_name_~",
                    columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                    principalSchema: "quartz",
                    principalTable: "qrtz_triggers",
                    principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "qrtz_simprop_triggers",
            schema: "quartz",
            columns: table => new
            {
                sched_name = table.Column<string>(type: "text", nullable: false),
                trigger_name = table.Column<string>(type: "text", nullable: false),
                trigger_group = table.Column<string>(type: "text", nullable: false),
                str_prop_1 = table.Column<string>(type: "text", nullable: true),
                str_prop_2 = table.Column<string>(type: "text", nullable: true),
                str_prop_3 = table.Column<string>(type: "text", nullable: true),
                int_prop_1 = table.Column<int>(type: "integer", nullable: true),
                int_prop_2 = table.Column<int>(type: "integer", nullable: true),
                long_prop_1 = table.Column<long>(type: "bigint", nullable: true),
                long_prop_2 = table.Column<long>(type: "bigint", nullable: true),
                dec_prop_1 = table.Column<decimal>(type: "numeric", nullable: true),
                dec_prop_2 = table.Column<decimal>(type: "numeric", nullable: true),
                bool_prop_1 = table.Column<bool>(type: "bool", nullable: true),
                bool_prop_2 = table.Column<bool>(type: "bool", nullable: true),
                time_zone_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qrtz_simprop_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                table.ForeignKey(
                    name: "FK_qrtz_simprop_triggers_qrtz_triggers_sched_name_trigger_name~",
                    columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                    principalSchema: "quartz",
                    principalTable: "qrtz_triggers",
                    principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AssetInstrumentLinks_InstrumentId",
            table: "AssetInstrumentLinks",
            column: "InstrumentId");

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

        migrationBuilder.CreateIndex(
            name: "idx_qrtz_ft_job_group",
            schema: "quartz",
            table: "qrtz_fired_triggers",
            column: "job_group");

        migrationBuilder.CreateIndex(
            name: "idx_qrtz_ft_job_name",
            schema: "quartz",
            table: "qrtz_fired_triggers",
            column: "job_name");

        migrationBuilder.CreateIndex(
            name: "idx_qrtz_ft_job_req_recovery",
            schema: "quartz",
            table: "qrtz_fired_triggers",
            column: "requests_recovery");

        migrationBuilder.CreateIndex(
            name: "idx_qrtz_ft_trig_group",
            schema: "quartz",
            table: "qrtz_fired_triggers",
            column: "trigger_group");

        migrationBuilder.CreateIndex(
            name: "idx_qrtz_ft_trig_inst_name",
            schema: "quartz",
            table: "qrtz_fired_triggers",
            column: "instance_name");

        migrationBuilder.CreateIndex(
            name: "idx_qrtz_ft_trig_name",
            schema: "quartz",
            table: "qrtz_fired_triggers",
            column: "trigger_name");

        migrationBuilder.CreateIndex(
            name: "idx_qrtz_ft_trig_nm_gp",
            schema: "quartz",
            table: "qrtz_fired_triggers",
            columns: new[] { "sched_name", "trigger_name", "trigger_group" });

        migrationBuilder.CreateIndex(
            name: "idx_qrtz_j_req_recovery",
            schema: "quartz",
            table: "qrtz_job_details",
            column: "requests_recovery");

        migrationBuilder.CreateIndex(
            name: "IX_qrtz_triggers_sched_name_job_name_job_group",
            schema: "quartz",
            table: "qrtz_triggers",
            columns: new[] { "sched_name", "job_name", "job_group" });

        migrationBuilder.CreateIndex(
            name: "idx_qrtz_t_next_fire_time",
            schema: "quartz",
            table: "qrtz_triggers",
            column: "next_fire_time");

        migrationBuilder.CreateIndex(
            name: "idx_qrtz_t_nft_st",
            schema: "quartz",
            table: "qrtz_triggers",
            columns: new[] { "next_fire_time", "trigger_state" });

        migrationBuilder.CreateIndex(
            name: "idx_qrtz_t_state",
            schema: "quartz",
            table: "qrtz_triggers",
            column: "trigger_state");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AssetInstrumentLinks");

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
            name: "qrtz_blob_triggers",
            schema: "quartz");

        migrationBuilder.DropTable(
            name: "qrtz_calendars",
            schema: "quartz");

        migrationBuilder.DropTable(
            name: "qrtz_cron_triggers",
            schema: "quartz");

        migrationBuilder.DropTable(
            name: "qrtz_fired_triggers",
            schema: "quartz");

        migrationBuilder.DropTable(
            name: "qrtz_locks",
            schema: "quartz");

        migrationBuilder.DropTable(
            name: "qrtz_paused_trigger_grps",
            schema: "quartz");

        migrationBuilder.DropTable(
            name: "qrtz_scheduler_state",
            schema: "quartz");

        migrationBuilder.DropTable(
            name: "qrtz_simple_triggers",
            schema: "quartz");

        migrationBuilder.DropTable(
            name: "qrtz_simprop_triggers",
            schema: "quartz");

        migrationBuilder.DropTable(
            name: "MarketDataInboxState");

        migrationBuilder.DropTable(
            name: "MarketDataOutboxState");

        migrationBuilder.DropTable(
            name: "qrtz_triggers",
            schema: "quartz");

        migrationBuilder.DropTable(
            name: "qrtz_job_details",
            schema: "quartz");
    }
}
