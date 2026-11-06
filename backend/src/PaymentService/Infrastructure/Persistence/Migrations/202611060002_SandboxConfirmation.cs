using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwiftRide.Payments.Infrastructure;

[DbContext(typeof(PayDb))]
[Migration("202611060002_SandboxConfirmation")]
public sealed class SandboxConfirmationMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "NextQueryAt",
            table: "SandboxPayments",
            type: "timestamp with time zone",
            nullable: true
        );
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastIpnAt",
            table: "SandboxPayments",
            type: "timestamp with time zone",
            nullable: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "NextQueryAt", table: "SandboxPayments");
        migrationBuilder.DropColumn(name: "LastIpnAt", table: "SandboxPayments");
    }
}
