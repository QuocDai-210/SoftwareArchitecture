using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using SwiftRide.Application.Persistence;
using SwiftRide.Application.Security;
using SwiftRide.Contracts;
using SwiftRide.Infrastructure;
using SwiftRide.Payments.Application.Sandbox;
using SwiftRide.Payments.Application.Wallets;
using SwiftRide.Payments.Domain;
using SwiftRide.Payments.Infrastructure;
using SwiftRide.Payments.Infrastructure.Sandbox;
using SwiftRide.Payments.Infrastructure.Wallets;
using SwiftRide.SharedKernel;
using Xunit;

public sealed class WalletDatabaseFactAttribute : FactAttribute
{
    public WalletDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")))
        {
            Skip = "Requires isolated PostgreSQL: scripts/check-wallet.ps1";
        }
    }
}

public sealed class WalletStorageTests
{
    private const string Secret = "wallet-test-secret";
    private static readonly Actor User = new("wallet-test-user", IsRider: true);

    [WalletDatabaseFact]
    [Trait("Category", "Integration")]
    [Trait("Category", "WalletIntegration")]
    public async Task WalletFundingIsAtomicIsolatedAndExactlyOnceOnRealPostgres()
    {
        var connection = Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!;
        var verifier = new SandboxVnpay(
            new HttpClient(),
            new SandboxVnpayOptions
            {
                Enabled = true,
                TmnCode = "TEST",
                HashSecret = Secret,
            },
            TimeProvider.System
        );
        var provider = new Mock<ISandboxVnpay>();
        provider
            .Setup(p => p.Checkout(It.IsAny<SandboxPayment>()))
            .Returns((SandboxPayment p) => verifier.Checkout(p));
        provider
            .Setup(p => p.Verify(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns((IReadOnlyDictionary<string, string> fields) => verifier.Verify(fields));
        var services = new ServiceCollection();
        services.AddDbContext<PayDb>(o => o.UseNpgsql(connection));
        services.AddScoped<ISandboxStore, SandboxStore>();
        services.AddScoped<IWalletAccounts, WalletAccounts>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork<PayDb>>();
        services.AddScoped<IWalletFunding, WalletFunding>();
        services.AddScoped<WalletQueries>();
        services.AddScoped<SandboxPayments>();
        services.AddSingleton(provider.Object);
        services.AddSingleton(TimeProvider.System);
        await using var root = services.BuildServiceProvider();
        await using (var scope = root.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayDb>();
            // This test refuses a database already containing financial records.
            Assert.False(await db.Database.CanConnectAsync() && await HasTables(db));
            await db.Database.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
        }

        async Task<T> Use<TService, T>(Func<TService, Task<T>> action)
            where TService : notnull
        {
            await using var scope = root.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<TService>());
        }
        Task<SandboxView> Create(long amount, string key) =>
            Use<SandboxPayments, SandboxView>(app => app.CreateTopUp(amount, key, User, default));
        Task<string> Notify(Dictionary<string, string> fields) =>
            Use<SandboxPayments, string>(app => app.Notify(fields, default));
        Task<WalletView> Balance() => Use<WalletQueries, WalletView>(q => q.Get(User, default));

        Assert.Equal(0, (await Balance()).Available);
        var driver = new Actor("wallet-test-driver", IsDriver: true);
        Assert.False((await Use<WalletQueries, WalletView>(q => q.Get(driver, default))).CanTopUp);
        await Assert.ThrowsAsync<RuleException>(() =>
            Use<SandboxPayments, SandboxView>(app =>
                app.CreateTopUp(10000, Guid.NewGuid().ToString(), driver, default)
            )
        );
        await Assert.ThrowsAsync<RuleException>(() =>
            Use<WalletQueries, WalletView>(q => q.Get(new Actor("ops", IsOps: true), default))
        );

        var key = Guid.NewGuid().ToString();
        var retries = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Create(10000, key)));
        Assert.Single(retries.Select(p => p.Reference).Distinct());
        var first = retries[0];
        Assert.Equal("WalletTopUp", first.Purpose);
        await Assert.ThrowsAsync<RuleException>(() => Create(20000, key));
        await Assert.ThrowsAsync<RuleException>(() =>
            Use<SandboxPayments, SandboxView>(app => app.Create(10000, key, User, default))
        );
        var second = await Create(20000, Guid.NewGuid().ToString());
        Assert.Equal("04", await Notify(Callback(first, "10001", amount: 20000)));
        var badSignature = Callback(first, "10001");
        badSignature["vnp_SecureHash"] = new string('0', 128);
        Assert.Equal("97", await Notify(badSignature));
        Assert.Equal(0, (await Balance()).Available);

        var confirmations = await Task.WhenAll(
            Enumerable
                .Range(0, 16)
                .Select(i =>
                    Notify(i % 2 == 0 ? Callback(first, "10001") : Callback(second, "10002"))
                )
        );
        Assert.Equal(2, confirmations.Count(code => code == "00"));
        Assert.Equal(14, confirmations.Count(code => code == "02"));
        Assert.Equal(30000, (await Balance()).Available);
        Assert.Equal(0, (await Balance()).Held);
        Assert.Equal(0, (await Balance()).PendingEarnings);

        var sandbox = await Use<SandboxPayments, SandboxView>(app =>
            app.Create(50000, Guid.NewGuid().ToString(), User, default)
        );
        Assert.Equal("00", await Notify(Callback(sandbox, "10003")));
        Assert.Equal(30000, (await Balance()).Available);
        var sandboxView = await Use<SandboxPayments, SandboxView>(app =>
            app.Get(sandbox.Reference, User, default)
        );
        Assert.Null(sandboxView.WalletCreditedAt);

        var rollback = await Create(30000, Guid.NewGuid().ToString());
        await using (var scope = root.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayDb>();
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE FUNCTION wallet_test_reject() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Injected storage failure'; END $$;
                CREATE TRIGGER wallet_test_failure BEFORE INSERT ON "WalletActivities"
                  FOR EACH ROW EXECUTE FUNCTION wallet_test_reject();
                """
            );
        }
        await Assert.ThrowsAsync<DbUpdateException>(() => Notify(Callback(rollback, "10004")));
        Assert.Equal(30000, (await Balance()).Available);
        Assert.Equal(
            "Pending",
            (
                await Use<SandboxPayments, SandboxView>(app =>
                    app.Get(rollback.Reference, User, default)
                )
            ).State
        );
        await using (var scope = root.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayDb>();
            Assert.Equal(2, await db.Journals.CountAsync());
            Assert.Equal(2, await db.WalletActivities.CountAsync());
            await db.Database.ExecuteSqlRawAsync(
                """
                DROP TRIGGER wallet_test_failure ON "WalletActivities";
                DROP FUNCTION wallet_test_reject();
                """
            );
        }
        Assert.Equal("00", await Notify(Callback(rollback, "10004")));
        Assert.Equal(60000, (await Balance()).Available);

        var race = await Create(40000, Guid.NewGuid().ToString());
        var reply = new TaskCompletionSource<SandboxReceipt?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider
            .Setup(p =>
                p.Query(
                    It.Is<SandboxPayment>(p => p.Reference == race.Reference),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(() =>
            {
                started.SetResult();
                return reply.Task;
            });
        var query = Use<SandboxPayments, SandboxView>(app =>
            app.Reconcile(race.Reference, User, default)
        );
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("00", await Notify(Callback(race, "10005")));
        reply.SetResult(verifier.Verify(Callback(race, "10005")));
        Assert.NotNull((await query).WalletCreditedAt);
        Assert.Equal(100000, (await Balance()).Available);

        var history = await Use<WalletQueries, PageResult<WalletActivity>>(q =>
            q.History(1, User, default)
        );
        Assert.Equal(4, history.Total);
        Assert.Equal(
            0,
            (
                await Use<WalletQueries, PageResult<WalletActivity>>(q =>
                    q.History(1, driver, default)
                )
            ).Total
        );
        await using (var scope = root.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayDb>();
            Assert.Equal(4, await db.Journals.CountAsync());
            Assert.Equal(0, await db.Wallets.CountAsync());
            Assert.Equal(0, await db.Payments.CountAsync());
            foreach (var journal in await db.Journals.ToListAsync())
            {
                Ledger.Validate(journal.Lines);
                Assert.Equal(2, journal.Lines.Count);
                Assert.Equal("WalletTopUp", journal.Kind);
            }
            await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync("UPDATE \"WalletAccounts\" SET \"Available\" = -1;")
            );
            await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync("DELETE FROM \"WalletActivities\";")
            );
            await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync("UPDATE \"Journals\" SET \"Kind\" = 'tampered';")
            );
        }
    }

    private static async Task<bool> HasTables(PayDb db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='public');";
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static Dictionary<string, string> Callback(
        SandboxView payment,
        string transaction,
        long? amount = null
    )
    {
        var fields = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = "TEST",
            ["vnp_TxnRef"] = payment.Reference,
            ["vnp_Amount"] = ((amount ?? payment.Amount) * 100).ToString(
                CultureInfo.InvariantCulture
            ),
            ["vnp_TransactionNo"] = transaction,
            ["vnp_ResponseCode"] = "00",
            ["vnp_TransactionStatus"] = "00",
        };
        fields["vnp_SecureHash"] = Convert.ToHexStringLower(
            HMACSHA512.HashData(
                Encoding.UTF8.GetBytes(Secret),
                Encoding.UTF8.GetBytes(SandboxVnpay.Canonical(fields))
            )
        );
        return fields;
    }
}
