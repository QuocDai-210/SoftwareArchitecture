using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwiftRide.Payments.Infrastructure;

[DbContext(typeof(PayDb))]
[Migration("202611140003_WalletPaymentDriver")]
public sealed class WalletPaymentDriverMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            UPDATE "Payments" AS payment
            SET "DriverId" = activity."OwnerId"
            FROM "Journals" AS journal
            JOIN "WalletActivities" AS activity ON activity."JournalId" = journal."Id"
            WHERE journal."PaymentId" = payment."Id"
              AND activity."Kind" = 'DriverEarning'
              AND payment."Method" = 'Wallet'
              AND payment."DriverId" IS NULL;
            """
        );

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep repaired ownership metadata when rolling back; no financial data is removed.
    }
}
