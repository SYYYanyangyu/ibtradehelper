using IBTrade.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBTrade.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261025000000_FixTradeTimeEastern")]
    public partial class FixTradeTimeEastern : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 存量成交时间是把 Flex 美东墙钟直接当成 UTC 存的：按美东（含夏令时）重新解释为真实 UTC
            migrationBuilder.Sql("""
                UPDATE trades
                SET "TradeTimeUtc" = ("TradeTimeUtc" AT TIME ZONE 'UTC') AT TIME ZONE 'America/New_York';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE trades
                SET "TradeTimeUtc" = ("TradeTimeUtc" AT TIME ZONE 'America/New_York') AT TIME ZONE 'UTC';
                """);
        }
    }
}
