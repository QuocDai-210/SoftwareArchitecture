using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwiftRide.Payments.Infrastructure;

[DbContext(typeof(PayDb))]
[Migration("202611140004_QuoteHoldJournals")]
public sealed class QuoteHoldJournalsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_Journals_PaymentId_Kind", "Journals");
        // Each quote can have a hold/release cycle. WalletActivities.Reference provides its
        // idempotency constraint; charge, settlement, earning and refund remain unique per trip.
        migrationBuilder.CreateIndex(
            name: "IX_Journals_PaymentId_Kind",
            table: "Journals",
            columns: ["PaymentId", "Kind"],
            unique: true,
            filter: "\"Kind\" NOT IN ('TripHold', 'TripHoldReleased')"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "Cannot discard quote hold cycles to restore the old index."
        );
}
