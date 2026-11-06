using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwiftRide.Payments.Infrastructure;

[DbContext(typeof(PayDb))]
[Migration("202611060001_SandboxPayments")]
public sealed class SandboxPaymentsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SandboxPayments",
            columns: table => new
            {
                Reference = table.Column<string>(type: "character varying(32)", maxLength: 32),
                OwnerId = table.Column<string>(type: "character varying(100)", maxLength: 100),
                RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100),
                Amount = table.Column<long>(type: "bigint"),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone"),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone"),
                State = table.Column<string>(type: "character varying(20)", maxLength: 20),
                ProviderTransaction = table.Column<string>(
                    type: "character varying(15)",
                    maxLength: 15,
                    nullable: true
                ),
                ConfirmedAt = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                NextCheckAt = table.Column<DateTimeOffset>(type: "timestamp with time zone"),
                CheckCount = table.Column<int>(type: "integer"),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SandboxPayments", x => x.Reference);
                table.CheckConstraint(
                    "CK_SandboxPayment_Amount",
                    "\"Amount\" BETWEEN 10000 AND 5000000"
                );
            }
        );
        migrationBuilder.CreateIndex(
            "IX_SandboxPayments_OwnerId_RequestKey",
            "SandboxPayments",
            new[] { "OwnerId", "RequestKey" },
            unique: true
        );
        migrationBuilder.CreateIndex(
            "IX_SandboxPayments_ProviderTransaction",
            "SandboxPayments",
            "ProviderTransaction",
            unique: true
        );
        migrationBuilder.CreateIndex(
            "IX_SandboxPayments_State_NextCheckAt",
            "SandboxPayments",
            new[] { "State", "NextCheckAt" }
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("SandboxPayments");
    }
}
