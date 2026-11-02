using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SwiftRide.Core;
using SwiftRide.Hosting;

namespace PaymentService;

public sealed class Payment
{
    public Guid Id { get; set; }
    public string RiderId { get; set; } = "";
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public string RequestHash { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string? RefundKey { get; set; }
    public string? RefundHash { get; set; }
    public string? Reason { get; set; }
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class Wallet
{
    public string RiderId { get; set; } = "";
    public decimal Balance { get; set; }
}
public sealed class Journal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentId { get; set; }
    public string Kind { get; set; } = "";
    public List<LedgerLine> Lines { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class ProviderOperation
{
    public string Key { get; set; } = "";
    public decimal Amount { get; set; }
    public string RequestHash { get; set; } = "";
    public string State { get; set; } = "new";
    public int Attempts { get; set; }
    public bool UnknownInjected { get; set; }
    public bool Recovered { get; set; }
}
public sealed class PayDb(DbContextOptions<PayDb> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Journal> Journals => Set<Journal>();
    public DbSet<ProviderOperation> ProviderOperations => Set<ProviderOperation>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Outbox();
        var p = b.Entity<Payment>(); p.HasKey(x => x.Id);
        p.Property(x => x.RiderId).HasMaxLength(100);
        p.Property(x => x.IdempotencyKey).HasMaxLength(100);
        p.HasIndex(x => x.IdempotencyKey).IsUnique();
        p.Property(x => x.Amount).HasPrecision(18, 0);
        p.Property(x => x.Status).HasConversion<string>(); p.Property(x => x.Method).HasConversion<string>();
        p.ToTable(t => t.HasCheckConstraint("CK_Payment_Amount", "\"Amount\" >= 0"));
        var w = b.Entity<Wallet>(); w.HasKey(x => x.RiderId);
        w.Property(x => x.Balance).HasPrecision(18, 0).IsConcurrencyToken();
        w.ToTable(t => t.HasCheckConstraint("CK_Wallet_Balance", "\"Balance\" >= 0"));
        var j = b.Entity<Journal>(); j.HasKey(x => x.Id); j.Property(x => x.Lines).JsonColumn();
        j.Property(x => x.Kind).HasMaxLength(150); j.HasIndex(x => new { x.PaymentId, x.Kind }).IsUnique();
        var op = b.Entity<ProviderOperation>(); op.HasKey(x => x.Key); op.Property(x => x.Key).HasMaxLength(150);
        op.Property(x => x.Amount).HasPrecision(18, 0);
    }
}
[DbContext(typeof(PayDb))]
[Migration("202611020001_Initial")]
public sealed class InitialMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ponytail: bootstrap v1; freeze generated SQL and add a snapshot before schema v2.
        using var db = new PayDb(new DbContextOptionsBuilder<PayDb>().UseNpgsql().Options);
        migrationBuilder.Sql(db.Database.GenerateCreateScript());
        migrationBuilder.Sql("""
            CREATE FUNCTION ledger_balanced(lines text) RETURNS boolean LANGUAGE sql IMMUTABLE AS $$
              SELECT COALESCE(count(*) >= 2 AND
                bool_and(COALESCE(jsonb_typeof(value->'amount') = 'number' AND (value->>'amount')::numeric >= 0
                  AND (value->>'amount')::numeric = trunc((value->>'amount')::numeric)
                  AND value->>'side' IN ('Debit','Credit') AND length(value->>'account') > 0, false)) AND
                sum(CASE value->>'side' WHEN 'Debit' THEN (value->>'amount')::numeric
                  WHEN 'Credit' THEN -(value->>'amount')::numeric END) = 0, false)
              FROM jsonb_array_elements(lines::jsonb)
            $$;
            ALTER TABLE "Journals" ADD CONSTRAINT "CK_Journal_Balanced" CHECK (ledger_balanced("Lines"));
            CREATE FUNCTION ledger_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN RAISE EXCEPTION 'Ledger is append-only'; END
            $$;
            CREATE TRIGGER journal_append_only BEFORE UPDATE OR DELETE ON "Journals"
              FOR EACH ROW EXECUTE FUNCTION ledger_immutable();
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Restore a backup instead of deleting financial data.");
}
