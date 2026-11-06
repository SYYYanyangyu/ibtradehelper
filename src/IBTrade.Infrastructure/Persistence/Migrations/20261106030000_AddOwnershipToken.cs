using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBTrade.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnershipToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ownership_token",
                table: "push_subscriptions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ownership_token",
                table: "push_subscriptions");
        }
    }
}
