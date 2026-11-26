namespace SwiftRide.Trips.Infrastructure;

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
        t.HasIndex(x => x.RiderId)
            .IsUnique()
            .HasFilter(
                "\"Status\" IN ('Requested','AwaitingRiderConfirmation',"
                    + "'ReservingDriver','AwaitingDriverAcceptance','AcceptingDriver',"
                    + "'DriverAccepted','EnRoute','PickedUp')"
            );
    }
}
