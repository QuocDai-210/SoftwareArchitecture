using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwiftRide.Trips.Infrastructure;

[DbContext(typeof(TripDb))]
[Migration("202611060004_WalletTripStatus")]
public sealed class WalletTripStatusMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) { }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
