using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwiftRide.Trips.Infrastructure;

[DbContext(typeof(TripDb))]
[Migration("202611060005_WalletHoldActiveTripIndex")]
public sealed class WalletHoldActiveTripIndexMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            "DROP INDEX IF EXISTS \"IX_Trips_RiderId\"; "
                + "CREATE UNIQUE INDEX \"IX_Trips_RiderId\" ON \"Trips\" (\"RiderId\") "
                + "WHERE \"Status\" IN ('Requested','AwaitingRiderConfirmation',"
                + "'WalletHoldPending','ReservingDriver','AwaitingDriverAcceptance',"
                + "'AcceptingDriver','DriverAccepted','EnRoute','PickedUp');"
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            "DROP INDEX IF EXISTS \"IX_Trips_RiderId\"; "
                + "CREATE UNIQUE INDEX \"IX_Trips_RiderId\" ON \"Trips\" (\"RiderId\") "
                + "WHERE \"Status\" IN ('Requested','AwaitingRiderConfirmation',"
                + "'ReservingDriver','AwaitingDriverAcceptance','AcceptingDriver',"
                + "'DriverAccepted','EnRoute','PickedUp');"
        );
}
