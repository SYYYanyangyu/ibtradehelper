using IBTrade.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBTrade.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261018105500_AddTradeMultiplier")]
    public partial class AddTradeMultiplier : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Multiplier",
                table: "trades",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            // 存量期权：按资产类型或 OCC 代码回填乘数，避免成交额少两位
            migrationBuilder.Sql("""
                UPDATE trades
                SET "Multiplier" = 100
                WHERE "Multiplier" = 1
                  AND (
                    "AssetType" = 2
                    OR "Symbol" ~ ' [0-9]{6}[CP][0-9]{8}$'
                  );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Multiplier",
                table: "trades");
        }
    }
}
