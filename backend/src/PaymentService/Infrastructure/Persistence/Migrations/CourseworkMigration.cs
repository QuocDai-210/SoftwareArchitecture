using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwiftRide.Payments.Infrastructure
{
    /// <inheritdoc />
    public partial class Coursework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Attempt",
                table: "Payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L
            );

            migrationBuilder.AddColumn<string>(
                name: "CheckoutReference",
                table: "Payments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "IsTopUp",
                table: "Payments",
                type: "boolean",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.CreateTable(
                name: "CheckoutAttempts",
                columns: table => new
                {
                    Reference = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(
                        type: "numeric(18,0)",
                        precision: 18,
                        scale: 0,
                        nullable: false
                    ),
                    CreatedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ProviderTransaction = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: true
                    ),
                    PayDate = table.Column<string>(
                        type: "character varying(14)",
                        maxLength: 14,
                        nullable: true
                    ),
                    BankCode = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: true
                    ),
                    RefundSubmitted = table.Column<bool>(type: "boolean", nullable: false),
                    RefundRequestedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutAttempts", x => x.Reference);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CheckoutReference",
                table: "Payments",
                column: "CheckoutReference",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutAttempts_PaymentId",
                table: "CheckoutAttempts",
                column: "PaymentId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CheckoutAttempts");

            migrationBuilder.DropIndex(name: "IX_Payments_CheckoutReference", table: "Payments");

            migrationBuilder.DropColumn(name: "Attempt", table: "Payments");

            migrationBuilder.DropColumn(name: "CheckoutReference", table: "Payments");

            migrationBuilder.DropColumn(name: "IsTopUp", table: "Payments");
        }
    }
}
