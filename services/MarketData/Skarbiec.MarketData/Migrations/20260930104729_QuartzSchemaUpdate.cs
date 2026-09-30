using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skarbiec.MarketData.Migrations;

/// <inheritdoc />
public partial class QuartzSchemaUpdate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "execution_group",
            schema: "quartz",
            table: "qrtz_triggers",
            type: "varchar(200)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "preferred_node",
            schema: "quartz",
            table: "qrtz_triggers",
            type: "varchar(200)",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "preferred_node_auto",
            schema: "quartz",
            table: "qrtz_triggers",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "retry_attempt",
            schema: "quartz",
            table: "qrtz_triggers",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "retry_policy",
            schema: "quartz",
            table: "qrtz_triggers",
            type: "varchar(250)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "execution_group",
            schema: "quartz",
            table: "qrtz_fired_triggers",
            type: "varchar(200)",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "qrtz_paused_job_grps",
            schema: "quartz",
            columns: table => new
            {
                sched_name = table.Column<string>(type: "text", nullable: false),
                job_group = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qrtz_paused_job_grps", x => new { x.sched_name, x.job_group });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "qrtz_paused_job_grps",
            schema: "quartz");

        migrationBuilder.DropColumn(
            name: "execution_group",
            schema: "quartz",
            table: "qrtz_triggers");

        migrationBuilder.DropColumn(
            name: "preferred_node",
            schema: "quartz",
            table: "qrtz_triggers");

        migrationBuilder.DropColumn(
            name: "preferred_node_auto",
            schema: "quartz",
            table: "qrtz_triggers");

        migrationBuilder.DropColumn(
            name: "retry_attempt",
            schema: "quartz",
            table: "qrtz_triggers");

        migrationBuilder.DropColumn(
            name: "retry_policy",
            schema: "quartz",
            table: "qrtz_triggers");

        migrationBuilder.DropColumn(
            name: "execution_group",
            schema: "quartz",
            table: "qrtz_fired_triggers");
    }
}
