using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwiftRide.Payments.Infrastructure
{
    /// <inheritdoc />
    public partial class Cod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CollectedAt",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CollectedBy",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CollectionHash",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CollectionKey",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CollectionReason",
                table: "Payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "DriverId",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RefundedAt",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "RefundedBy",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CollectedAt", table: "Payments");

            migrationBuilder.DropColumn(name: "CollectedBy", table: "Payments");

            migrationBuilder.DropColumn(name: "CollectionHash", table: "Payments");

            migrationBuilder.DropColumn(name: "CollectionKey", table: "Payments");

            migrationBuilder.DropColumn(name: "CollectionReason", table: "Payments");

            migrationBuilder.DropColumn(name: "DriverId", table: "Payments");

            migrationBuilder.DropColumn(name: "RefundedAt", table: "Payments");

            migrationBuilder.DropColumn(name: "RefundedBy", table: "Payments");
        }
    }
}
