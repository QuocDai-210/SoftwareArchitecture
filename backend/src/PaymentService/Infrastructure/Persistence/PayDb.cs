namespace SwiftRide.Payments.Infrastructure;

public sealed class PayDb(DbContextOptions<PayDb> options) : DbContext(options)
{
    public DbSet<SandboxPayment> SandboxPayments => Set<SandboxPayment>();
    public DbSet<WalletAccount> WalletAccounts => Set<WalletAccount>();
    public DbSet<WalletActivity> WalletActivities => Set<WalletActivity>();
    public DbSet<WalletWithdrawal> WalletWithdrawals => Set<WalletWithdrawal>();
    public DbSet<CheckoutAttempt> CheckoutAttempts => Set<CheckoutAttempt>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Journal> Journals => Set<Journal>();
    public DbSet<ProviderOperation> ProviderOperations => Set<ProviderOperation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Outbox();
        var sandbox = b.Entity<SandboxPayment>();
        sandbox.HasKey(x => x.Reference);
        sandbox.Property(x => x.Reference).HasMaxLength(32);
        sandbox.Property(x => x.OwnerId).HasMaxLength(100);
        sandbox.Property(x => x.RequestKey).HasMaxLength(100);
        sandbox.Property(x => x.State).HasMaxLength(20);
        sandbox.Property(x => x.ProviderTransaction).HasMaxLength(15);
        sandbox.Property(x => x.Purpose).HasMaxLength(20).HasDefaultValue("Sandbox");
        sandbox.HasIndex(x => new { x.OwnerId, x.RequestKey }).IsUnique();
        sandbox.HasIndex(x => x.ProviderTransaction).IsUnique();
        sandbox.HasIndex(x => new { x.State, x.NextCheckAt });
        sandbox.ToTable(t =>
            t.HasCheckConstraint("CK_SandboxPayment_Amount", "\"Amount\" BETWEEN 10000 AND 5000000")
        );
        var account = b.Entity<WalletAccount>();
        account.HasKey(x => x.OwnerId);
        account.Property(x => x.OwnerId).HasMaxLength(100);
        account.Property(x => x.Available).IsConcurrencyToken();
        account.Property(x => x.Held).IsConcurrencyToken();
        account.Property(x => x.PendingEarnings).IsConcurrencyToken();
        account.ToTable(t =>
            t.HasCheckConstraint(
                "CK_WalletAccount_Nonnegative",
                "\"Available\" >= 0 AND \"Held\" >= 0 AND \"PendingEarnings\" >= 0"
            )
        );
        var activity = b.Entity<WalletActivity>();
        activity.HasKey(x => x.Reference);
        activity.Property(x => x.Reference).HasMaxLength(100);
        activity.Property(x => x.OwnerId).HasMaxLength(100);
        activity.Property(x => x.Kind).HasMaxLength(30);
        activity.HasIndex(x => new { x.OwnerId, x.CreatedAt });
        activity.HasIndex(x => x.JournalId).IsUnique();
        activity
            .HasOne<WalletAccount>()
            .WithMany()
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        activity
            .HasOne<Journal>()
            .WithMany()
            .HasForeignKey(x => x.JournalId)
            .OnDelete(DeleteBehavior.Restrict);
        activity.ToTable(t =>
            t.HasCheckConstraint(
                "CK_WalletActivity_Amounts",
                "\"Amount\" > 0 AND \"AvailableAfter\" >= 0 AND \"HeldAfter\" >= 0 AND \"PendingAfter\" >= 0"
            )
        );
        var withdrawal = b.Entity<WalletWithdrawal>();
        withdrawal.HasKey(x => x.Id);
        withdrawal.Property(x => x.OwnerId).HasMaxLength(100);
        withdrawal.Property(x => x.RequestKey).HasMaxLength(100);
        withdrawal.Property(x => x.RequestHash).HasMaxLength(64);
        withdrawal.Property(x => x.State).HasMaxLength(20).IsConcurrencyToken();
        withdrawal.Property(x => x.ReviewedBy).HasMaxLength(100);
        withdrawal.Property(x => x.DecisionReason).HasMaxLength(200);
        withdrawal.HasIndex(x => new { x.OwnerId, x.RequestKey }).IsUnique();
        withdrawal.HasIndex(x => new { x.State, x.RequestedAt });
        withdrawal
            .HasOne<WalletAccount>()
            .WithMany()
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        withdrawal.ToTable(t =>
            t.HasCheckConstraint(
                "CK_WalletWithdrawal_Amount",
                "\"Amount\" BETWEEN 10000 AND 5000000"
            )
        );
        var p = b.Entity<Payment>();
        p.HasKey(x => x.Id);
        p.Property(x => x.RiderId).HasMaxLength(100);
        p.Property(x => x.DriverId).HasMaxLength(100);
        p.Property(x => x.CollectedBy).HasMaxLength(100);
        p.Property(x => x.CollectionKey).HasMaxLength(100);
        p.Property(x => x.CollectionHash).HasMaxLength(100);
        p.Property(x => x.CollectionReason).HasMaxLength(200);
        p.Property(x => x.RefundedBy).HasMaxLength(100);
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
