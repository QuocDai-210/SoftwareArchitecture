using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Moq;
using SwiftRide.Application.Persistence;
using SwiftRide.Application.Security;
using SwiftRide.Payments.Application.Sandbox;
using SwiftRide.Payments.Application.Wallets;
using SwiftRide.Payments.Domain;
using SwiftRide.Payments.Infrastructure;
using SwiftRide.Payments.Infrastructure.Sandbox;
using SwiftRide.SharedKernel;
using Xunit;

public sealed class SandboxPaymentTests
{
    private const string Reference = "0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 3, 0, 0, TimeSpan.Zero);
    private static readonly Actor User = new("sandbox-user", IsRider: true);

    private static Dictionary<string, string> Callback() =>
        new()
        {
            ["vnp_TmnCode"] = "TEST",
            ["vnp_TxnRef"] = Reference,
            ["vnp_Amount"] = "1000000",
            ["vnp_TransactionNo"] = "123456",
            ["vnp_TransactionStatus"] = "00",
            ["vnp_ResponseCode"] = "00",
            // Independent fixture generated with Python urllib.parse + hmac, not this adapter.
            ["vnp_SecureHash"] =
                "ad7a8a94e97cbc01ab308b4aebd4a00f49ad02d44ecd92daf89edb3589c86444cc6e65e560063e3a394757a8c2914b98d75ee7c0419fe55ba5f34083b6d206ec",
        };

    private static SandboxVnpay Provider(HttpMessageHandler? handler = null) =>
        new(
            handler is null ? new HttpClient() : new HttpClient(handler),
            new SandboxVnpayOptions
            {
                Enabled = true,
                TmnCode = "TEST",
                HashSecret = "test-secret",
            },
            new FixedClock()
        );

    [Fact]
    public void CallbackMatchesIndependentFixtureAndRejectsTampering()
    {
        var provider = Provider();
        Assert.Equal(10000, provider.Verify(Callback())!.Amount);
        foreach (
            var field in new[]
            {
                "vnp_Amount",
                "vnp_TmnCode",
                "vnp_TxnRef",
                "vnp_TransactionStatus",
            }
        )
        {
            var fields = Callback();
            fields[field] += "1";
            Assert.Null(provider.Verify(fields));
        }
        var invalid = Callback();
        invalid["vnp_SecureHash"] = new string('x', 128);
        Assert.Null(provider.Verify(invalid));
        invalid.Remove("vnp_SecureHash");
        Assert.Null(provider.Verify(invalid));
    }

    [Fact]
    public void CheckoutUsesVndTimes100VietnamTimeAndEncodedReturnUrl()
    {
        var url = Provider()
            .Checkout(
                new SandboxPayment
                {
                    Reference = Reference,
                    Amount = 10000,
                    CreatedAt = Now,
                    ExpiresAt = Now.AddMinutes(15),
                }
            );
        Assert.StartsWith(SandboxVnpay.PaymentUrl + "?", url);
        Assert.Contains("vnp_Amount=1000000&", url);
        Assert.Contains("vnp_CreateDate=20261106100000&", url);
        Assert.Contains("vnp_ExpireDate=20261106101500&", url);
        Assert.Contains("vnp_ReturnUrl=http%3A%2F%2Flocalhost%3A3000%2Fpayments%2Freturn&", url);
        Assert.Equal(
            "vnp_A=a+b%2Bc&vnp_Z=%C4%91",
            SandboxVnpay.Canonical(
                new Dictionary<string, string>
                {
                    ["vnp_Z"] = "đ",
                    ["vnp_A"] = "a b+c",
                    ["ignored"] = "x",
                }
            )
        );
    }

    [Fact]
    public async Task CallbackIsIdempotentAndInvalidAmountCannotChangeState()
    {
        var store = new MemoryStore();
        var provider = new Mock<ISandboxVnpay>();
        var receipt = new SandboxReceipt(Reference, 10000, "123456", "00", "00");
        provider
            .Setup(p => p.Verify(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(receipt);
        var app = App(store, provider.Object);
        Assert.Equal("01", await app.Notify(Callback(), default));
        var payment = new SandboxPayment { Reference = Reference, Amount = 20000 };
        store.Add(payment);
        Assert.Equal("04", await app.Notify(Callback(), default));
        Assert.Equal("Pending", payment.State);
        payment.Amount = 10000;
        Assert.Equal("00", await app.Notify(Callback(), default));
        Assert.Equal("02", await app.Notify(Callback(), default));
        Assert.Equal("Succeeded", payment.State);
        Assert.Equal("123456", payment.ProviderTransaction);
    }

    [Fact]
    public async Task CreationRetriesReuseReferenceAndRejectChangedAmountOrOtherOwner()
    {
        var store = new MemoryStore();
        var app = App(store, Provider());
        var key = Guid.NewGuid().ToString();
        var first = await app.Create(10000, key, User, default);
        var retry = await app.Create(10000, key, User, default);
        Assert.Equal(first.Reference, retry.Reference);
        Assert.Single(store.Items);
        await Assert.ThrowsAsync<RuleException>(() => app.Create(20000, key, User, default));
        await Assert.ThrowsAsync<RuleException>(() => app.Create(9999, key, User, default));
        await Assert.ThrowsAsync<RuleException>(() =>
            app.Create(10000, key, new Actor("driver", IsDriver: true), default)
        );
        var denied = await Assert.ThrowsAsync<RuleException>(() =>
            app.Get(first.Reference, new Actor("other"), default)
        );
        Assert.Equal(ErrorKind.NotFound, denied.Kind);
    }

    [Fact]
    public async Task UnverifiableQueryNeverSettlesAndQueryRespectsCooldown()
    {
        var store = new MemoryStore();
        var payment = new SandboxPayment
        {
            Reference = Reference,
            Amount = 10000,
            OwnerId = User.Id,
        };
        store.Add(payment);
        var provider = new Mock<ISandboxVnpay>();
        provider
            .Setup(p => p.Query(payment, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SandboxReceipt?)null);
        var app = App(store, provider.Object);
        await app.Reconcile(Reference, User, default);
        await app.Reconcile(Reference, User, default);
        Assert.Equal("Pending", payment.State);
        provider.Verify(p => p.Query(payment, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("00", "00", "Succeeded")]
    [InlineData("00", "99", "NeedsReview")]
    [InlineData("01", "00", "NeedsReview")]
    [InlineData("02", "24", "Failed")]
    [InlineData("07", "00", "NeedsReview")]
    public void StatusAndResponseAreBothRequired(string status, string response, string expected)
    {
        var payment = new SandboxPayment();
        payment.Record(status, response, "123456", Now);
        Assert.Equal(expected, payment.State);
    }

    [Fact]
    public async Task InteractiveCheckBypassesWorkerScheduleButSharesShortLease()
    {
        var store = new MemoryStore();
        var payment = new SandboxPayment
        {
            Reference = Reference,
            OwnerId = User.Id,
            Amount = 10000,
            NextCheckAt = Now.AddHours(1),
            CheckCount = 12,
        };
        store.Add(payment);
        var provider = new Mock<ISandboxVnpay>();
        provider
            .Setup(p => p.Query(payment, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SandboxReceipt?)null);
        var clock = new FixedClock();
        var app = App(store, provider.Object, clock);
        await app.Reconcile(Reference, default);
        provider.Verify(p => p.Query(payment, It.IsAny<CancellationToken>()), Times.Never);
        var view = await app.Reconcile(Reference, User, default);
        Assert.Equal(Now.AddSeconds(15), view.NextQueryAt);
        await app.Reconcile(Reference, User, default);
        provider.Verify(p => p.Query(payment, It.IsAny<CancellationToken>()), Times.Once);
        clock.Current = Now.AddSeconds(15);
        await app.Reconcile(Reference, User, default);
        provider.Verify(p => p.Query(payment, It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal(12, payment.CheckCount);
        Assert.Equal(Now.AddHours(1), payment.NextCheckAt);
    }

    [Fact]
    public async Task QueryRateLimitDefersQueriesButDoesNotBlockIpn()
    {
        var store = new MemoryStore();
        var payment = new SandboxPayment
        {
            Reference = Reference,
            OwnerId = User.Id,
            Amount = 10000,
        };
        store.Add(payment);
        var provider = new Mock<ISandboxVnpay>();
        provider
            .Setup(p => p.Query(payment, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SandboxQueryDeferredException());
        provider
            .Setup(p => p.Verify(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(new SandboxReceipt(Reference, 10000, "123456", "00", "00"));
        var app = App(store, provider.Object);
        var view = await app.Reconcile(Reference, User, default);
        Assert.Equal(Now.AddMinutes(5), view.NextQueryAt);
        Assert.Equal("Pending", view.State);
        await app.Reconcile(Reference, User, default);
        provider.Verify(p => p.Query(payment, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("00", await app.Notify(Callback(), default));
        Assert.Equal("Succeeded", payment.State);
        Assert.Equal(Now, payment.LastIpnAt);
    }

    [Fact]
    public async Task ConcurrentQueryDoesNotUndoIpnAndOnlyOneQueryIsSent()
    {
        var store = new MemoryStore();
        var payment = new SandboxPayment
        {
            Reference = Reference,
            OwnerId = User.Id,
            Amount = 10000,
        };
        store.Add(payment);
        var provider = new Mock<ISandboxVnpay>();
        var response = new TaskCompletionSource<SandboxReceipt?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        provider.Setup(p => p.Query(payment, It.IsAny<CancellationToken>())).Returns(response.Task);
        provider
            .Setup(p => p.Verify(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(new SandboxReceipt(Reference, 10000, "123456", "00", "00"));
        var app = App(store, provider.Object);
        var query = app.Reconcile(Reference, User, default);
        await app.Reconcile(Reference, User, default);
        Assert.Equal("00", await app.Notify(Callback(), default));
        response.SetResult(new SandboxReceipt(Reference, 10000, "0", "02", "24"));
        Assert.Equal("Succeeded", (await query).State);
        provider.Verify(p => p.Query(payment, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnotherOwnerCannotTriggerProviderQuery()
    {
        var store = new MemoryStore();
        store.Add(new SandboxPayment { Reference = Reference, OwnerId = User.Id });
        var provider = new Mock<ISandboxVnpay>(MockBehavior.Strict);
        await Assert.ThrowsAsync<RuleException>(() =>
            App(store, provider.Object).Reconcile(Reference, new Actor("other"), default)
        );
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProviderDuplicateQueryResponseIsNotAPaymentFailure()
    {
        var provider = Provider(
            new ResponseHandler(new Dictionary<string, string> { ["vnp_ResponseCode"] = "94" })
        );
        await Assert.ThrowsAsync<SandboxQueryDeferredException>(() =>
            provider.Query(new SandboxPayment { Reference = Reference, CreatedAt = Now }, default)
        );
    }

    [Fact]
    public async Task QueryChecksSignedResponseAndOriginalTransactionIdentity()
    {
        var fields = Callback();
        fields["vnp_ResponseId"] = "response-id";
        fields["vnp_Command"] = "querydr";
        fields["vnp_Message"] = "Success";
        fields["vnp_TransactionType"] = "01";
        fields["vnp_OrderInfo"] = "sandbox";
        string[] order =
        [
            "vnp_ResponseId",
            "vnp_Command",
            "vnp_ResponseCode",
            "vnp_Message",
            "vnp_TmnCode",
            "vnp_TxnRef",
            "vnp_Amount",
            "vnp_BankCode",
            "vnp_PayDate",
            "vnp_TransactionNo",
            "vnp_TransactionType",
            "vnp_TransactionStatus",
            "vnp_OrderInfo",
            "vnp_PromotionCode",
            "vnp_PromotionAmount",
        ];
        fields["vnp_SecureHash"] = Convert.ToHexStringLower(
            HMACSHA512.HashData(
                Encoding.UTF8.GetBytes("test-secret"),
                Encoding.UTF8.GetBytes(
                    string.Join("|", order.Select(k => fields.GetValueOrDefault(k, "")))
                )
            )
        );
        var provider = Provider(new ResponseHandler(fields));
        var payment = new SandboxPayment { Reference = Reference, CreatedAt = Now };
        Assert.NotNull(await provider.Query(payment, default));
        fields["vnp_TransactionStatus"] = "02";
        Assert.Null(await provider.Query(payment, default));
    }

    [Fact]
    public void SnapshotMatchesAndSandboxSchemaIsSeparateFromWallets()
    {
        using var db = new PayDb(
            new DbContextOptionsBuilder<PayDb>().UseNpgsql("Host=localhost;Database=unused").Options
        );
        Assert.False(db.Database.HasPendingModelChanges());
        var entity = db.Model.FindEntityType(typeof(SandboxPayment))!;
        Assert.Contains(
            entity.GetIndexes(),
            i =>
                i.IsUnique
                && i.Properties.Count == 1
                && i.Properties[0].Name == "ProviderTransaction"
        );
        Assert.Empty(entity.GetForeignKeys());
    }

    private static SandboxPayments App(
        MemoryStore store,
        ISandboxVnpay provider,
        TimeProvider? clock = null
    )
    {
        var tx = new Mock<ITransaction>();
        tx.Setup(t => t.Commit(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var unit = new Mock<IUnitOfWork>();
        unit.Setup(u => u.Begin(It.IsAny<CancellationToken>())).ReturnsAsync(tx.Object);
        unit.Setup(u => u.Save(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return new SandboxPayments(
            store,
            provider,
            unit.Object,
            clock ?? new FixedClock(),
            new Mock<IWalletFunding>(MockBehavior.Strict).Object
        );
    }

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = Now;

        public override DateTimeOffset GetUtcNow() => Current;
    }

    private sealed class ResponseHandler(Dictionary<string, string> fields) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(fields) }
            );
    }

    private sealed class MemoryStore : ISandboxStore
    {
        public List<SandboxPayment> Items { get; } = [];

        public Task Lock(string key, CancellationToken ct) => Task.CompletedTask;

        public Task<SandboxPayment?> Find(string reference, CancellationToken ct) =>
            Task.FromResult(Items.SingleOrDefault(p => p.Reference == reference));

        public Task<SandboxPayment?> FindRequest(string owner, string key, CancellationToken ct) =>
            Task.FromResult(Items.SingleOrDefault(p => p.OwnerId == owner && p.RequestKey == key));

        public Task<IReadOnlyList<string>> Due(DateTimeOffset now, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public void Add(SandboxPayment payment) => Items.Add(payment);
    }
}
