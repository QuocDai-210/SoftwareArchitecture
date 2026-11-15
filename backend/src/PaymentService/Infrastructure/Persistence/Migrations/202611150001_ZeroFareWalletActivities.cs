using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwiftRide.Payments.Infrastructure;

[DbContext(typeof(PayDb))]
[Migration("202611150001_ZeroFareWalletActivities")]
public sealed class ZeroFareWalletActivitiesMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_WalletActivity_Amounts", "WalletActivities");
        migrationBuilder.AddCheckConstraint(
            "CK_WalletActivity_Amounts",
            "WalletActivities",
            "(\"Amount\" > 0 OR (\"Amount\" = 0 AND \"Kind\" IN ('TripHold', 'TripHoldReleased', 'TripSettled', 'DriverEarning'))) AND \"AvailableAfter\" >= 0 AND \"HeldAfter\" >= 0 AND \"PendingAfter\" >= 0"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Cannot discard the audit trail of zero-fare trips.");
}
