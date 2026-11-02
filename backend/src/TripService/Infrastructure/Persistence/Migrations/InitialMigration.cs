using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwiftRide.Trips.Infrastructure;

[DbContext(typeof(TripDb))]
[Migration("202611020001_Initial")]
public sealed class InitialMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var reader = new StreamReader(
            typeof(InitialMigration).Assembly.GetManifestResourceStream("InitialSchema.sql")!
        );
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restore a backup instead of deleting trip data.");
}
