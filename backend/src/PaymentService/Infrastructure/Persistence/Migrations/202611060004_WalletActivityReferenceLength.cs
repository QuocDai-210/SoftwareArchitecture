using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwiftRide.Payments.Infrastructure;

[DbContext(typeof(PayDb))]
[Migration("202611060004_WalletActivityReferenceLength")]
public sealed class WalletActivityReferenceLengthMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            "ALTER TABLE \"WalletActivities\" "
                + "ALTER COLUMN \"Reference\" TYPE character varying(100);"
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "Wallet ledger references cannot be shortened after trip activities are recorded."
        );
}
