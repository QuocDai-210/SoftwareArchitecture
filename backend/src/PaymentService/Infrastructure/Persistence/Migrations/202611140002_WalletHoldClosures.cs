using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwiftRide.Payments.Infrastructure;

[DbContext(typeof(PayDb))]
[Migration("202611140002_WalletHoldClosures")]
public sealed class WalletHoldClosuresMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WalletHoldClosures",
            columns: table => new
            {
                TripId = table.Column<Guid>(type: "uuid", nullable: false),
                QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                RiderId = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: false
                ),
                Amount = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
                table.PrimaryKey("PK_WalletHoldClosures", x => new { x.TripId, x.QuoteId })
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("WalletHoldClosures");
}
