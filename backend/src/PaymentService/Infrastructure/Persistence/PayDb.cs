namespace SwiftRide.Payments.Infrastructure;

public sealed class PayDb(DbContextOptions<PayDb> options) : DbContext(options)
{
    public DbSet<WalletAccount> WalletAccounts => Set<WalletAccount>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Journal> Journals => Set<Journal>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Outbox();
        var account = b.Entity<WalletAccount>();
        account.HasKey(x => x.OwnerId);
        account.Property(x => x.OwnerId).HasMaxLength(100);
        account.ToTable(t => t.HasCheckConstraint("CK_Balance", "\"Available\" >= 0"));
        var p = b.Entity<Payment>();
        p.HasKey(x => x.Id);
        p.Property(x => x.RiderId).HasMaxLength(100);
        p.Property(x => x.DriverId).HasMaxLength(100);
        p.Property(x => x.RefundedBy).HasMaxLength(100);
        p.Property(x => x.IdempotencyKey).HasMaxLength(100);
        p.HasIndex(x => x.IdempotencyKey).IsUnique();
        p.Property(x => x.Amount).HasPrecision(18, 0);
        p.Property(x => x.Status).HasConversion<string>();
        p.Property(x => x.Method).HasConversion<string>();
        p.ToTable(t => t.HasCheckConstraint("CK_Payment_Amount", "\"Amount\" >= 0"));
        var j = b.Entity<Journal>();
        j.HasKey(x => x.Id);
        j.Property(x => x.Lines).JsonColumn();
        j.Property(x => x.Kind).HasMaxLength(150);
        j.HasIndex(x => new { x.PaymentId, x.Kind }).IsUnique();
        j.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId);
    }
}
