using System.Text.Json;
using SwiftRide.Contracts;
using SwiftRide.Matching.Domain;
using SwiftRide.Payments.Domain;
using SwiftRide.SharedKernel;
using SwiftRide.Trips.Domain;
using Xunit;

public sealed class CoreTests
{
    [Fact]
    public void DomainContracts()
    {
        var checks = 0;
        void Check(bool value, string name)
        {
            if (!value)
                Assert.Fail(name);
            checks++;
            Console.WriteLine($"PASS {name}");
        }
        void Reject(Action action, string name, int status = 409)
        {
            try
            {
                action();
                Assert.Fail($"{name}: accepted invalid input");
            }
            catch (RuleException ex)
            {
                Check(
                    ex.Kind
                        == (
                            status switch
                            {
                                400 => ErrorKind.Validation,
                                403 => ErrorKind.Forbidden,
                                404 => ErrorKind.NotFound,
                                _ => ErrorKind.Conflict,
                            }
                        ),
                    name
                );
            }
        }
        try
        {
            _ = JsonSerializer.Deserialize<VersionRequest>("{}");
            throw new Exception("Missing expectedVersion accepted");
        }
        catch (JsonException)
        {
            Check(true, "missing expectedVersion rejected before command");
        }
        try
        {
            _ = JsonSerializer.Deserialize<LocationRequest>("{}");
            throw new Exception("Missing coordinates accepted");
        }
        catch (JsonException)
        {
            Check(true, "missing coordinates do not silently become zero");
        }
        var calculator = new FareCalculator(
            new PromoPricing(new SurgePricing(new StandardPricing()))
        );
        var sample = new FareInput(5, 10, Surge: 1.5m, Toll: 10000, Promotion: 0.9m);
        Check(
            new FareCalculator(new FreePricing()).Calculate(sample) == 0,
            "OCP: inject new strategy without changing calculator"
        );
        Check(
            calculator.Calculate(sample) == 91575,
            "PDF formula: toll before promo and tax = 91,575 VND"
        );
        Check(
            calculator.Calculate(new(1, 1, PerKm: 1, PerMinute: 0, Tax: 0.5m)) == 2,
            "VND midpoint rounds away from zero"
        );
        Check(
            calculator.Calculate(new(5, 10, Promotion: 0)) == 0,
            "100% promotion gives zero fare"
        );
        Reject(
            () => calculator.Calculate(sample with { DistanceKm = -1 }),
            "negative distance",
            400
        );
        Reject(
            () => calculator.Calculate(sample with { Promotion = 1.1m }),
            "invalid promotion",
            400
        );
        Reject(() => calculator.Calculate(sample with { Surge = 0 }), "invalid surge", 400);
        Reject(() => calculator.Calculate(sample with { Tax = -0.1m }), "negative tax", 400);
        var strategies = new IPricingStrategy[]
        {
            new StandardPricing(),
            new SurgePricing(new StandardPricing()),
            new PromoPricing(new SurgePricing(new StandardPricing())),
        };
        foreach (var strategy in strategies)
        {
            foreach (var distance in new[] { 0.1m, 1m, 100m })
            foreach (var promo in new[] { 0m, 0.5m, 1m })
                Check(
                    strategy.Calculate(sample with { DistanceKm = distance, Promotion = promo })
                        >= 0,
                    $"LSP {strategy.GetType().Name} d={distance} promo={promo}"
                );
            Reject(
                () => strategy.Calculate(sample with { Toll = -1 }),
                $"LSP {strategy.GetType().Name} rejects invalid input",
                400
            );
        }
        Reject(() => new Point(double.NaN, 106, "Bad").Validate(), "NaN coordinates", 400);
        Reject(() => new Point(91, 106, "Bad").Validate(), "latitude outside earth", 400);
        Reject(
            () => new Point(10, double.PositiveInfinity, "Bad").Validate(),
            "infinite longitude",
            400
        );
        var pickup = new Point(10.7769, 106.7009, "Nhà hát Thành phố");
        var destination = new Point(10.7599, 106.6822, "Đại học Sài Gòn");
        var route = Routes.Estimate(pickup, destination);
        Check(
            route.DistanceKm is > 3 and < 5 && route.DurationMinutes > 0,
            "reproducible demo route"
        );
        Reject(() => Routes.Estimate(pickup, pickup), "identical pickup and destination", 400);
        var trip = new Trip { Id = Guid.NewGuid(), RiderId = "rider" };
        Reject(() => trip.Move(TripStatus.PickedUp), "cannot pickup before driver accepts");
        trip.OfferQuote(
            "d1",
            new(Guid.NewGuid(), 91575, "VND", sample, DateTimeOffset.UtcNow.AddMinutes(2))
        );
        Reject(
            () => trip.ConfirmQuote(Guid.NewGuid(), DateTimeOffset.UtcNow),
            "cannot confirm different quote"
        );
        Reject(
            () => trip.ConfirmQuote(trip.Quote!.Id, DateTimeOffset.UtcNow.AddMinutes(3)),
            "cannot confirm expired quote"
        );
        trip.ConfirmQuote(trip.Quote!.Id, DateTimeOffset.UtcNow);
        Check(trip.ReservationId != null, "quote confirmation creates reservation identity");
        trip.Move(TripStatus.AwaitingDriverAcceptance);
        trip.Move(TripStatus.AcceptingDriver);
        trip.Move(TripStatus.DriverAccepted);
        Reject(() => trip.Move(TripStatus.Paid), "cannot mark paid without ride and payment");
        trip.Move(TripStatus.EnRoute);
        trip.Move(TripStatus.PickedUp);
        trip.Move(TripStatus.DroppedOff);
        trip.Move(TripStatus.PaymentPending);
        trip.Move(TripStatus.PaymentReview);
        Check(trip.Status != TripStatus.Paid, "payment failure remains review, never successful");
        trip.Move(TripStatus.PaymentPending);
        trip.Move(TripStatus.Paid);
        Reject(() => trip.Move(TripStatus.Cancelled), "paid trip is terminal");
        Reject(() => trip.CheckVersion(trip.Version - 1), "stale command rejected");
        Check(trip.History.Count == trip.Version + 1, "all successful transitions recorded once");
        var rejected = new Trip();
        rejected.OfferQuote(
            "d1",
            new(Guid.NewGuid(), 91575, "VND", sample, DateTimeOffset.UtcNow.AddMinutes(2))
        );
        rejected.ConfirmQuote(rejected.Quote!.Id, DateTimeOffset.UtcNow);
        rejected.OfferDriver(DateTimeOffset.UtcNow.AddSeconds(60));
        for (var i = 1; i <= 4; i++)
        {
            rejected.RejectDriver();
            Check(
                rejected.Status == (i <= 3 ? TripStatus.Requested : TripStatus.Failed),
                $"driver rejection {i}: exact k=3 boundary"
            );
            if (i <= 3)
            {
                rejected.OfferQuote(
                    $"d{i + 1}",
                    new(Guid.NewGuid(), 91575, "VND", sample, DateTimeOffset.UtcNow.AddMinutes(2))
                );
                rejected.ConfirmQuote(rejected.Quote!.Id, DateTimeOffset.UtcNow);
                rejected.OfferDriver(DateTimeOffset.UtcNow.AddSeconds(60));
            }
        }
        Check(
            rejected.ExcludedDrivers.Distinct().Count() == 4,
            "rejected drivers excluded from subsequent attempts"
        );
        foreach (var final in new[] { TripStatus.Paid, TripStatus.Cancelled, TripStatus.Failed })
        foreach (var target in Enum.GetValues<TripStatus>())
            Reject(() => TripState.For(final).Ensure(target), $"{final} blocks {target}");
        foreach (var method in Enum.GetValues<PaymentMethod>())
        {
            var lines = Ledger.Charge(
                method switch
                {
                    PaymentMethod.Card => "CardClearing",
                    PaymentMethod.Wallet => "WalletLiability:rider",
                    _ => "CashHeldByDriver:driver",
                },
                91575
            );
            Ledger.Validate(lines);
            Ledger.Validate(Ledger.Reverse(lines));
            Check(
                lines.Sum(x => x.Side == EntrySide.Debit ? x.Amount : -x.Amount) == 0,
                $"{method} balanced charge"
            );
            Check(
                lines
                    .Concat(Ledger.Reverse(lines))
                    .GroupBy(x => x.Account)
                    .All(g => g.Sum(x => x.Side == EntrySide.Debit ? x.Amount : -x.Amount) == 0),
                $"{method} full compensation restores every account"
            );
        }
        Reject(
            () => Ledger.Validate([new("A", EntrySide.Debit, 100), new("B", EntrySide.Credit, 99)]),
            "unbalanced ledger rejected"
        );
        Reject(() => Ledger.Charge("CardClearing", -1), "negative payment rejected", 400);
        Reject(() => Ledger.Charge("CardClearing", 1.5m), "fractional VND rejected", 400);
        Reject(
            () => RequestKeys.Same(RequestKeys.Hash("old"), RequestKeys.Hash("new")),
            "same key different payload rejected"
        );
        Reject(() => RequestKeys.Validate("bad\nkey"), "control character in key rejected", 400);
        RequestKeys.Same(RequestKeys.Hash("same"), RequestKeys.Hash("same"));
        Check(true, "identical payload replay accepted");
        Console.WriteLine($"Core checks passed: {checks}");
    }

    [Fact]
    public Task PaymentContracts() =>
        ApplicationChecks.Run((value, name) => Assert.True(value, name));

    [Fact]
    public void MatchingAndMapContracts() =>
        MapChecks.Run((value, name) => Assert.True(value, name));
}

sealed class FreePricing : IPricingStrategy
{
    public decimal Calculate(FareInput input)
    {
        input.Validate();
        return 0;
    }
}
