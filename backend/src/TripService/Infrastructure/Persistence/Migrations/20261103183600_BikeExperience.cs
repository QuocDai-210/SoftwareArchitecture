using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwiftRide.Trips.Infrastructure
{
    /// <inheritdoc />
    public partial class BikeExperience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PickupNote",
                table: "Trips",
                type: "character varying(240)",
                maxLength: 240,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RatedAt",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "Trips",
                type: "integer",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Review",
                table: "Trips",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "RiderName",
                table: "Trips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "RiderPhone",
                table: "Trips",
                type: "character varying(15)",
                maxLength: 15,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_Trips_Review",
                table: "Trips",
                sql: "(\"Rating\" IS NULL AND \"Review\" IS NULL AND \"RatedAt\" IS NULL) OR (\"Rating\" IS NOT NULL AND \"Rating\" BETWEEN 1 AND 5 AND \"RatedAt\" IS NOT NULL AND \"Status\" IN ('PaymentPending','PaymentReview','Paid'))"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException(
                "Restore a backup instead of deleting rider notes and reviews."
            );
    }
}
