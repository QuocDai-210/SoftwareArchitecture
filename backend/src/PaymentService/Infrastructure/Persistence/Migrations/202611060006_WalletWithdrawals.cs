using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwiftRide.Payments.Infrastructure;

[DbContext(typeof(PayDb))]
[Migration("202611060006_WalletWithdrawals")]
public sealed class WalletWithdrawalsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WalletWithdrawals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid"),
                OwnerId = table.Column<string>(type: "character varying(100)", maxLength: 100),
                RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100),
                RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64),
                Amount = table.Column<long>(type: "bigint"),
                State = table.Column<string>(type: "character varying(20)", maxLength: 20),
                RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone"),
                ReviewedAt = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                ReviewedBy = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: true
                ),
                DecisionReason = table.Column<string>(
                    type: "character varying(200)",
                    maxLength: 200,
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WalletWithdrawals", x => x.Id);
                table.CheckConstraint(
                    "CK_WalletWithdrawal_Amount",
                    "\"Amount\" BETWEEN 10000 AND 5000000"
                );
                table.ForeignKey(
                    "FK_WalletWithdrawals_WalletAccounts_OwnerId",
                    x => x.OwnerId,
                    "WalletAccounts",
                    "OwnerId",
                    onDelete: ReferentialAction.Restrict
                );
            }
        );
        migrationBuilder.CreateIndex(
            "IX_WalletWithdrawals_OwnerId_RequestKey",
            "WalletWithdrawals",
            new[] { "OwnerId", "RequestKey" },
            unique: true
        );
        migrationBuilder.CreateIndex(
            "IX_WalletWithdrawals_State_RequestedAt",
            "WalletWithdrawals",
            new[] { "State", "RequestedAt" }
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "Restore a backup instead of deleting wallet withdrawal records."
        );
}
