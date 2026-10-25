using IBTrade.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBTrade.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261025010000_AddOrderContractFields")]
    public partial class AddOrderContractFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "SecType", table: "orders", type: "character varying(8)", maxLength: 8, nullable: true);
            migrationBuilder.AddColumn<string>(name: "Expiry", table: "orders", type: "character varying(8)", maxLength: 8, nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "Strike", table: "orders", type: "numeric(18,6)", precision: 18, scale: 6, nullable: true);
            migrationBuilder.AddColumn<string>(name: "Right", table: "orders", type: "character varying(1)", maxLength: 1, nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "Multiplier", table: "orders", type: "numeric(18,6)", precision: 18, scale: 6, nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SecType", table: "orders");
            migrationBuilder.DropColumn(name: "Expiry", table: "orders");
            migrationBuilder.DropColumn(name: "Strike", table: "orders");
            migrationBuilder.DropColumn(name: "Right", table: "orders");
            migrationBuilder.DropColumn(name: "Multiplier", table: "orders");
        }
    }
}
