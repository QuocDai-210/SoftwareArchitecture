using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwiftRide.Payments.Infrastructure;

[DbContext(typeof(PayDb))]
[Migration("202611060003_WalletAccounts")]
public sealed class WalletAccountsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "SandboxPayments"
              ADD COLUMN "Purpose" character varying(20) NOT NULL DEFAULT 'Sandbox',
              ADD COLUMN "WalletCreditedAt" timestamp with time zone;
            CREATE TABLE "WalletAccounts" (
              "OwnerId" character varying(100) PRIMARY KEY,
              "Available" bigint NOT NULL,
              "Held" bigint NOT NULL,
              "PendingEarnings" bigint NOT NULL,
              CONSTRAINT "CK_WalletAccount_Nonnegative"
                CHECK ("Available" >= 0 AND "Held" >= 0 AND "PendingEarnings" >= 0)
            );
            CREATE TABLE "WalletActivities" (
              "Reference" character varying(100) PRIMARY KEY,
              "OwnerId" character varying(100) NOT NULL,
              "JournalId" uuid NOT NULL,
              "Kind" character varying(30) NOT NULL,
              "Amount" bigint NOT NULL,
              "AvailableAfter" bigint NOT NULL,
              "HeldAfter" bigint NOT NULL,
              "PendingAfter" bigint NOT NULL,
              "CreatedAt" timestamp with time zone NOT NULL,
              CONSTRAINT "CK_WalletActivity_Amounts"
                CHECK ("Amount" > 0 AND "AvailableAfter" >= 0 AND "HeldAfter" >= 0 AND "PendingAfter" >= 0),
              CONSTRAINT "FK_WalletActivities_WalletAccounts_OwnerId"
                FOREIGN KEY ("OwnerId") REFERENCES "WalletAccounts" ("OwnerId") ON DELETE RESTRICT,
              CONSTRAINT "FK_WalletActivities_Journals_JournalId"
                FOREIGN KEY ("JournalId") REFERENCES "Journals" ("Id") ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX "IX_WalletActivities_JournalId" ON "WalletActivities" ("JournalId");
            CREATE INDEX "IX_WalletActivities_OwnerId_CreatedAt" ON "WalletActivities" ("OwnerId", "CreatedAt");
            CREATE TRIGGER wallet_activity_append_only BEFORE UPDATE OR DELETE ON "WalletActivities"
              FOR EACH ROW EXECUTE FUNCTION ledger_immutable();
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "Restore a backup instead of deleting wallet financial data."
        );
}
