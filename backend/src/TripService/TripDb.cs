using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SwiftRide.Core;
using SwiftRide.Hosting;

namespace TripService;

public sealed class TripDb(DbContextOptions<TripDb> options) : DbContext(options)
{
    public DbSet<Trip> Trips => Set<Trip>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Outbox();
        var t = b.Entity<Trip>();
        t.HasKey(x => x.Id);
        t.Property(x => x.RiderId).HasMaxLength(100);
        t.Property(x => x.DriverId).HasMaxLength(100);
        t.Property(x => x.IdempotencyKey).HasMaxLength(100);
        t.Property(x => x.Status).HasConversion<string>().HasMaxLength(50);
        t.Property(x => x.Method).HasConversion<string>();
        t.Property(x => x.Version).IsConcurrencyToken();
        t.Property(x => x.Pickup).JsonColumn();
        t.Property(x => x.Destination).JsonColumn();
        t.Property(x => x.Quote).JsonColumn();
        t.Property(x => x.ExcludedDrivers).JsonColumn();
        t.Property(x => x.History).JsonColumn();
        t.HasIndex(x => new { x.RiderId, x.IdempotencyKey }).IsUnique();
        t.HasIndex(x => new { x.Status, x.UpdatedAt });
        t.HasIndex(x => x.RiderId).IsUnique().HasFilter("[Status] IN ('Requested','AwaitingRiderConfirmation','ReservingDriver','AwaitingDriverAcceptance','AcceptingDriver','DriverAccepted','EnRoute','PickedUp')");
    }
}
public interface ITripReader { Task<Trip> Get(Guid id, CancellationToken ct); }
public interface ITripWriter { Task Save(CancellationToken ct); }
public sealed class TripRepository(TripDb db) : ITripReader, ITripWriter
{
    public async Task<Trip> Get(Guid id, CancellationToken ct) => await db.Trips.SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new RuleException("Không tìm thấy chuyến.", 404);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
[DbContext(typeof(TripDb))]
[Migration("202611020001_Initial")]
public sealed class InitialMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ponytail: bootstrap v1; freeze generated SQL and add a snapshot before schema v2.
        using var db = new TripDb(new DbContextOptionsBuilder<TripDb>().UseSqlServer().Options);
        migrationBuilder.Sql(db.Database.GenerateCreateScript());
    }
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Restore a backup instead of deleting trip data.");
}
