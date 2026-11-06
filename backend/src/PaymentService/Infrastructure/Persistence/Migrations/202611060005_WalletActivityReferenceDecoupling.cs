using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwiftRide.Payments.Infrastructure;

[DbContext(typeof(PayDb))]
[Migration("202611060005_WalletActivityReferenceDecoupling")]
public sealed class WalletActivityReferenceDecouplingMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            "ALTER TABLE \"WalletActivities\" "
                + "DROP CONSTRAINT IF EXISTS \"FK_WalletActivities_SandboxPayments_Reference\";"
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "Wallet activities for trips must remain independent from sandbox payment references."
        );
}
