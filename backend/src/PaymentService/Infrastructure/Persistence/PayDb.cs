namespace SwiftRide.Payments.Infrastructure;

public sealed class PayDb(DbContextOptions<PayDb> options) : DbContext(options)
{
    public DbSet<CheckoutAttempt> CheckoutAttempts => Set<CheckoutAttempt>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Journal> Journals => Set<Journal>();
    public DbSet<ProviderOperation> ProviderOperations => Set<ProviderOperation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Outbox();
        var p = b.Entity<Payment>();
        p.HasKey(x => x.Id);
        p.Property(x => x.RiderId).HasMaxLength(100);
        p.Property(x => x.IdempotencyKey).HasMaxLength(100);
        p.HasIndex(x => x.IdempotencyKey).IsUnique();
        p.Property(x => x.Amount).HasPrecision(18, 0);
        p.Property(x => x.Status).HasConversion<string>();
        p.Property(x => x.Method).HasConversion<string>();
        p.ToTable(t => t.HasCheckConstraint("CK_Payment_Amount", "\"Amount\" >= 0"));
        p.Property(x => x.CheckoutReference).HasMaxLength(32);
        p.HasIndex(x => x.CheckoutReference).IsUnique();
        var checkout = b.Entity<CheckoutAttempt>();
        checkout.HasKey(x => x.Reference);
        checkout.Property(x => x.Reference).HasMaxLength(32);
        checkout.Property(x => x.Amount).HasPrecision(18, 0);
        checkout.HasIndex(x => x.PaymentId);
        checkout.Property(x => x.ProviderTransaction).HasMaxLength(30);
        checkout.Property(x => x.PayDate).HasMaxLength(14);
        checkout.Property(x => x.BankCode).HasMaxLength(30);
        var w = b.Entity<Wallet>();
        w.HasKey(x => x.RiderId);
        w.Property(x => x.Balance).HasPrecision(18, 0).IsConcurrencyToken();
        w.ToTable(t => t.HasCheckConstraint("CK_Wallet_Balance", "\"Balance\" >= 0"));
        var j = b.Entity<Journal>();
        j.HasKey(x => x.Id);
        j.Property(x => x.Lines).JsonColumn();
        j.Property(x => x.Kind).HasMaxLength(150);
        j.HasIndex(x => new { x.PaymentId, x.Kind }).IsUnique();
        var op = b.Entity<ProviderOperation>();
        op.HasKey(x => x.Key);
        op.Property(x => x.Key).HasMaxLength(150);
        op.Property(x => x.Amount).HasPrecision(18, 0);
    }
}
