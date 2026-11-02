using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IBTrade.Infrastructure.Persistence.Migrations;

// Earlier migrations assumed this column had already been added manually.
[DbContext(typeof(AppDbContext))]
[Migration("20261029000000_EnsurePositionDailyPnl")]
public class EnsurePositionDailyPnl : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE \"PositionDailySnapshots\" ADD COLUMN IF NOT EXISTS \"DailyPnl\" numeric NOT NULL DEFAULT 0;");

    // Preserve historical PnL: existing databases may have this column before this repair.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
