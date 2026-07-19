using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IBTrade.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDataSourceProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 存量数据按 Flex 报表口径回填（此前混写无溯源；Gateway 同步改为只刷市价）
            migrationBuilder.AddColumn<string>(
                name: "PriceSource",
                table: "positions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Flex");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "positions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Flex");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "DailySnapshots",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Flex");

            migrationBuilder.CreateTable(
                name: "data_feed_statuses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Feed = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Environment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    LastSuccessUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AccountId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ConnectedPort = table.Column<int>(type: "integer", nullable: true),
                    Message = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_feed_statuses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_data_feed_statuses_Feed_Environment",
                table: "data_feed_statuses",
                columns: new[] { "Feed", "Environment" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_feed_statuses");

            migrationBuilder.DropColumn(
                name: "PriceSource",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "DailySnapshots");
        }
    }
}
