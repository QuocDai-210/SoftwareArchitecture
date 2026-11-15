using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwiftRide.Trips.Infrastructure;

[DbContext(typeof(TripDb))]
[Migration("202611140001_LocalAccounts")]
public sealed class LocalAccountsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "Latitude",
            table: "UserProfiles",
            type: "double precision",
            nullable: true
        );
        migrationBuilder.AddColumn<double>(
            name: "Longitude",
            table: "UserProfiles",
            type: "double precision",
            nullable: true
        );
        migrationBuilder.CreateTable(
            name: "LocalAccounts",
            columns: table => new
            {
                Id = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: false
                ),
                Username = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: false
                ),
                PasswordHash = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                Role = table.Column<string>(
                    type: "character varying(20)",
                    maxLength: 20,
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LocalAccounts", x => x.Id);
                table.CheckConstraint("CK_LocalAccount_Role", "\"Role\" IN ('rider','driver')");
            }
        );
        migrationBuilder.CreateIndex(
            name: "IX_LocalAccounts_Username",
            table: "LocalAccounts",
            column: "Username",
            unique: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restore a backup instead of deleting account data.");
}
