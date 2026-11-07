using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IBTrade.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPushNotification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    endpoint = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    p256dh = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    auth = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    enable_scheduled_briefs = table.Column<bool>(type: "boolean", nullable: false),
                    enable_breaking_news = table.Column<bool>(type: "boolean", nullable: false),
                    scheduled_brief_times = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_notified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    last_error_message = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_histories",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    body = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    tag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    subscriptions_sent = table.Column<int>(type: "integer", nullable: false),
                    success_count = table.Column<int>(type: "integer", nullable: false),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_histories", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_endpoint",
                table: "push_subscriptions",
                column: "endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_is_active",
                table: "push_subscriptions",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_is_active_enable_scheduled_briefs",
                table: "push_subscriptions",
                columns: new[] { "is_active", "enable_scheduled_briefs" });

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_is_active_enable_breaking_news",
                table: "push_subscriptions",
                columns: new[] { "is_active", "enable_breaking_news" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_histories_sent_at_utc",
                table: "notification_histories",
                column: "sent_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_notification_histories_content_hash",
                table: "notification_histories",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "IX_notification_histories_type_sent_at_utc",
                table: "notification_histories",
                columns: new[] { "type", "sent_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "push_subscriptions");

            migrationBuilder.DropTable(
                name: "notification_histories");
        }
    }
}
