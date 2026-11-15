namespace SwiftRide.Trips.Infrastructure;

public sealed class TripDb(DbContextOptions<TripDb> options) : DbContext(options)
{
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<LocalAccount> LocalAccounts => Set<LocalAccount>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Outbox();
        var profile = b.Entity<UserProfile>();
        profile.HasKey(x => x.Id);
        profile.Property(x => x.Id).HasMaxLength(100);
        profile.Property(x => x.Name).HasMaxLength(100);
        profile.Property(x => x.Phone).HasMaxLength(15);
        profile.Property(x => x.Latitude);
        profile.Property(x => x.Longitude);
        var account = b.Entity<LocalAccount>();
        account.HasKey(x => x.Id);
        account.Property(x => x.Id).HasMaxLength(100);
        account.Property(x => x.Username).HasMaxLength(100).IsRequired();
        account.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        account.Property(x => x.Role).HasMaxLength(20).IsRequired();
        account.ToTable(t =>
            t.HasCheckConstraint("CK_LocalAccount_Role", "\"Role\" IN ('rider','driver')")
        );
        account.HasIndex(x => x.Username).IsUnique();
        var t = b.Entity<Trip>();
        t.HasKey(x => x.Id);
        t.Property(x => x.RiderId).HasMaxLength(100);
        t.Property(x => x.DriverId).HasMaxLength(100);
        t.Property(x => x.RiderName).HasMaxLength(100).HasDefaultValue("");
        t.Property(x => x.RiderPhone).HasMaxLength(15).HasDefaultValue("");
        t.Property(x => x.PickupNote).HasMaxLength(240).HasDefaultValue("");
        t.Property(x => x.Review).HasMaxLength(500);
        t.ToTable("Trips", table =>
            table.HasCheckConstraint(
                "CK_Trips_Review",
                "(\"Rating\" IS NULL AND \"Review\" IS NULL AND \"RatedAt\" IS NULL) OR "
                    + "(\"Rating\" IS NOT NULL AND \"Rating\" BETWEEN 1 AND 5 "
                    + "AND \"RatedAt\" IS NOT NULL AND "
                    + "\"Status\" IN ('PaymentPending','PaymentReview','Paid'))"
            )
        );
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
                "\"Status\" IN ('Requested','AwaitingRiderConfirmation','WalletHoldPending',"
                    + "'ReservingDriver','AwaitingDriverAcceptance','AcceptingDriver',"
                    + "'DriverAccepted','EnRoute','PickedUp')"
            );
    }
}
