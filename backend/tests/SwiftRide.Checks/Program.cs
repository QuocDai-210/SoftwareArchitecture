using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SwiftRide.Contracts;
using SwiftRide.Matching.Domain;
using SwiftRide.Payments.Domain;
using SwiftRide.SharedKernel;
using SwiftRide.Trips.Domain;

var checks = 0;
void Check(bool value, string name)
{
    if (!value)
        throw new Exception($"FAIL: {name}");
    checks++;
    Console.WriteLine($"PASS {name}");
}
void Reject(Action action, string name, int status = 409)
{
    try
    {
        action();
        throw new Exception($"FAIL: {name}: accepted invalid input");
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
var calculator = new FareCalculator(new PromoPricing(new SurgePricing(new StandardPricing())));
var sample = new FareInput(5, 10, Surge: 1.5m, Toll: 10000, Promotion: 0.9m);
Check(
    new FareCalculator(new FreePricing()).Calculate(sample) == 0,
    "OCP: inject new strategy without changing calculator"
);
Check(calculator.Calculate(sample) == 91575, "PDF formula: toll before promo and tax = 91,575 VND");
Check(
    calculator.Calculate(new(1, 1, PerKm: 1, PerMinute: 0, Tax: 0.5m)) == 2,
    "VND midpoint rounds away from zero"
);
Check(calculator.Calculate(new(5, 10, Promotion: 0)) == 0, "100% promotion gives zero fare");
Reject(() => calculator.Calculate(sample with { DistanceKm = -1 }), "negative distance", 400);
Reject(() => calculator.Calculate(sample with { Promotion = 1.1m }), "invalid promotion", 400);
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
            strategy.Calculate(sample with { DistanceKm = distance, Promotion = promo }) >= 0,
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
Reject(() => new Point(10, double.PositiveInfinity, "Bad").Validate(), "infinite longitude", 400);
var pickup = new Point(10.7769, 106.7009, "Nhà hát Thành phố");
var destination = new Point(10.7599, 106.6822, "Đại học Sài Gòn");
var route = Routes.Estimate(pickup, destination);
Check(route.DistanceKm is > 3 and < 5 && route.DurationMinutes > 0, "reproducible demo route");
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
        method == PaymentMethod.Card ? "CardClearing" : "WalletLiability:rider",
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
await ApplicationChecks.Run(Check);
Console.WriteLine($"Core checks passed: {checks}");
if (!args.Contains("--integration"))
    return;
await Integration.Run();

sealed class FreePricing : IPricingStrategy
{
    public decimal Calculate(FareInput input)
    {
        input.Validate();
        return 0;
    }
}

static class Integration
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
    private static readonly string Base =
        Environment.GetEnvironmentVariable("GATEWAY_URL") ?? "http://localhost:8080";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly Point Pickup = new(10.7769, 106.7009, "Nhà hát Thành phố");
    private static readonly Point Destination = new(10.7599, 106.6822, "Đại học Sài Gòn");
    private static readonly Dictionary<string, string> Tokens = [];

    private static void Assert(bool value, string name)
    {
        if (!value)
            throw new Exception("FAIL integration: " + name);
        Console.WriteLine("PASS integration " + name);
    }

    public static async Task Run()
    {
        foreach (
            var user in new[]
            {
                "rider1",
                "rider2",
                "driver1",
                "driver2",
                "driver3",
                "driver4",
                "ops1",
            }
        )
            Tokens[user] = await Token(user);
        using (var unauthorized = await Http.GetAsync(Base + "/trips"))
            Assert(
                unauthorized.StatusCode == HttpStatusCode.Unauthorized,
                "gateway rejects anonymous"
            );
        await Online();
        var key = Guid.NewGuid().ToString();
        var trip = await Send(
            "rider1",
            "POST",
            "/trips",
            new CreateTripRequest(Pickup, Destination, PaymentMethod.Wallet, null),
            key
        );
        var id = trip.GetProperty("id").GetGuid();
        var duplicate = await Send(
            "rider1",
            "POST",
            "/trips",
            new CreateTripRequest(Pickup, Destination, PaymentMethod.Wallet, null),
            key
        );
        Assert(duplicate.GetProperty("id").GetGuid() == id, "booking idempotency");
        await Expect("rider2", "GET", $"/trips/{id}", null, 403);
        await Expect(
            "rider1",
            "POST",
            "/trips",
            new CreateTripRequest(Pickup, Destination, PaymentMethod.Card, "demo-success"),
            409,
            key
        );
        trip = await Wait(id, "AwaitingRiderConfirmation");
        await Expect("driver1", "GET", $"/trips/{id}", null, 403);
        await Expect(
            "driver1",
            "POST",
            $"/trips/{id}/driver-accept",
            new VersionRequest(trip.GetProperty("version").GetInt64()),
            403
        );
        await Expect(
            "rider2",
            "POST",
            $"/trips/{id}/cancel",
            new VersionRequest(trip.GetProperty("version").GetInt64()),
            403
        );
        await Expect(
            "rider1",
            "PUT",
            "/drivers/me/location",
            new LocationRequest(Pickup.Latitude, Pickup.Longitude),
            403
        );
        await Expect(
            "driver1",
            "PUT",
            "/drivers/me/location",
            new LocationRequest(91, Pickup.Longitude),
            400
        );
        await Expect(
            "rider1",
            "POST",
            $"/trips/{id}/confirm-quote",
            new { quoteId = trip.GetProperty("quote").GetProperty("id").GetGuid() },
            400
        );
        await Expect("rider1", "POST", "/payments", new { }, 403);
        await Expect(
            "rider1",
            "POST",
            $"/trips/{id}/confirm-quote",
            new
            {
                expectedVersion = -1,
                quoteId = trip.GetProperty("quote").GetProperty("id").GetGuid(),
            },
            409
        );
        var walletBefore = (await Send("rider1", "GET", "/wallets/me"))
            .GetProperty("balance")
            .GetDecimal();
        var fare = trip.GetProperty("quote").GetProperty("amount").GetDecimal();
        await Complete(id, trip);
        var payment = await Send("rider1", "GET", $"/payments/{id}");
        Assert(payment.GetProperty("status").GetString() == "Settled", "wallet settles");
        var balance = (await Send("rider1", "GET", "/wallets/me"))
            .GetProperty("balance")
            .GetDecimal();
        Assert(balance == walletBefore - fare, "wallet debited exactly once");
        await CheckLedger(id, 1);
        await DatabaseGuards(id);
        var refundKey = Guid.NewGuid().ToString();
        await Send(
            "ops1",
            "POST",
            $"/payments/{id}/refund",
            new RefundRequest("Integration full refund"),
            refundKey
        );
        await Send(
            "ops1",
            "POST",
            $"/payments/{id}/refund",
            new RefundRequest("Integration full refund"),
            refundKey
        );
        await CheckLedger(id, 2);
        balance = (await Send("rider1", "GET", "/wallets/me")).GetProperty("balance").GetDecimal();
        Assert(balance == walletBefore, "refund restores wallet exactly once");
        await Expect(
            "rider1",
            "POST",
            $"/payments/{id}/refund",
            new RefundRequest("Not authorized"),
            403,
            Guid.NewGuid().ToString()
        );
        foreach (
            var scenario in new[]
            {
                "demo-success",
                "demo-transient",
                "demo-unknown",
                "demo-compensate",
                "demo-decline",
                "demo-unavailable",
            }
        )
        {
            await Online();
            var t = await Send(
                "rider1",
                "POST",
                "/trips",
                new CreateTripRequest(Pickup, Destination, PaymentMethod.Card, scenario),
                Guid.NewGuid().ToString()
            );
            var tid = t.GetProperty("id").GetGuid();
            t = await Wait(tid, "AwaitingRiderConfirmation");
            var expected = scenario is "demo-compensate" or "demo-decline" or "demo-unavailable"
                ? "PaymentReview"
                : "Paid";
            await Complete(tid, t, expected);
            if (scenario != "demo-unavailable")
                await CheckLedger(
                    tid,
                    scenario == "demo-compensate" ? 2
                        : scenario == "demo-decline" ? 0
                        : 1
                );
            if (scenario == "demo-unavailable")
            {
                await Send("ops1", "POST", $"/payments/{tid}/demo-recover", new { });
                var review = await Send("rider1", "GET", $"/trips/{tid}");
                await Act("ops1", tid, "retry-payment", review);
                await Wait(tid, "Paid");
                await CheckLedger(tid, 1);
            }
            Console.WriteLine("PASS integration card " + scenario);
        }
        await Online();
        var cancelled = await Send(
            "rider1",
            "POST",
            "/trips",
            new CreateTripRequest(Pickup, Destination, PaymentMethod.Wallet, null),
            Guid.NewGuid().ToString()
        );
        id = cancelled.GetProperty("id").GetGuid();
        cancelled = await Wait(id, "AwaitingRiderConfirmation");
        cancelled = await Act("rider1", id, "cancel", cancelled);
        Assert(cancelled.GetProperty("status").GetString() == "Cancelled", "rider refuses quote");
        await Online();
        var rejected = await Send(
            "rider1",
            "POST",
            "/trips",
            new CreateTripRequest(Pickup, Destination, PaymentMethod.Wallet, null),
            Guid.NewGuid().ToString()
        );
        id = rejected.GetProperty("id").GetGuid();
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await Online();
            rejected = await Wait(id, "AwaitingRiderConfirmation");
            await Confirm(id, rejected);
            rejected = await Wait(id, "AwaitingDriverAcceptance");
            rejected = await Act(DriverUser(rejected), id, "driver-reject", rejected);
            Assert(
                rejected.GetProperty("status").GetString()
                    == (attempt <= 3 ? "Requested" : "Failed"),
                "rejection boundary " + attempt
            );
        }
        await Race();
        await BookingRateLimit();
        Console.WriteLine("Integration checks passed. Fault recovery: python3 scripts/faults.py");
    }

    private static async Task BookingRateLimit()
    {
        for (var i = 0; i < 10; i++)
            await Expect("driver4", "POST", "/trips", new { }, 403);
        foreach (var path in new[] { "/trips", "/trips/", "/TRIPS/" })
            await Expect("driver4", "POST", path, new { }, 429);
    }

    private static async Task DatabaseGuards(Guid paymentId)
    {
        async Task RejectSql(string sql, string expected)
        {
            var start = new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (
                var argument in new[]
                {
                    "compose",
                    "exec",
                    "-T",
                    "postgres",
                    "psql",
                    "-U",
                    "payment",
                    "-d",
                    "PayDB",
                    "-v",
                    "ON_ERROR_STOP=1",
                    "-c",
                    sql,
                }
            )
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var diagnostic = await output + await error;
            Assert(
                process.ExitCode != 0 && diagnostic.Contains(expected, StringComparison.Ordinal),
                "DB guard " + expected
            );
        }
        await RejectSql(
            $"BEGIN; UPDATE \"Journals\" SET \"Kind\" = 'Edited' WHERE \"PaymentId\" = '{paymentId}'; ROLLBACK;",
            "Ledger is append-only"
        );
        await RejectSql(
            "BEGIN; UPDATE \"Wallets\" SET \"Balance\" = -1 WHERE \"RiderId\" = '11111111-1111-1111-1111-000000000001'; ROLLBACK;",
            "CK_Wallet_Balance"
        );
        var lines =
            "[{\"account\":\"A\",\"side\":\"Debit\",\"amount\":100},{\"account\":\"B\",\"side\":\"Credit\",\"amount\":99}]";
        await RejectSql(
            $"BEGIN; INSERT INTO \"Journals\" (\"Id\",\"PaymentId\",\"Kind\",\"Lines\",\"CreatedAt\") VALUES ('{Guid.NewGuid()}','{paymentId}','Invalid','{lines}',now()); ROLLBACK;",
            "CK_Journal_Balanced"
        );
    }

    private static async Task Race()
    {
        await Online();
        for (var i = 2; i <= 4; i++)
            await Send(
                $"driver{i}",
                "PUT",
                "/drivers/me/availability",
                new AvailabilityRequest(false)
            );
        var a = await Send(
            "rider1",
            "POST",
            "/trips",
            new CreateTripRequest(Pickup, Destination, PaymentMethod.Wallet, null),
            Guid.NewGuid().ToString()
        );
        var b = await Send(
            "rider2",
            "POST",
            "/trips",
            new CreateTripRequest(Pickup, Destination, PaymentMethod.Wallet, null),
            Guid.NewGuid().ToString()
        );
        var aid = a.GetProperty("id").GetGuid();
        var bid = b.GetProperty("id").GetGuid();
        a = await Wait(aid, "AwaitingRiderConfirmation", "rider1");
        b = await Wait(bid, "AwaitingRiderConfirmation", "rider2");
        await Task.WhenAll(Confirm(aid, a, "rider1"), Confirm(bid, b, "rider2"));
        var end = DateTimeOffset.UtcNow.AddSeconds(40);
        do
        {
            a = await Send("rider1", "GET", $"/trips/{aid}");
            b = await Send("rider2", "GET", $"/trips/{bid}");
            if (new[] { a, b }.Any(x => x.GetProperty("status").GetString() == "Failed"))
                break;
            await Task.Delay(1000);
        } while (DateTimeOffset.UtcNow < end);
        Assert(
            new[] { a, b }.Count(x =>
                x.GetProperty("status").GetString() == "AwaitingDriverAcceptance"
            ) == 1,
            "single atomic reservation under competing riders"
        );
        foreach (var pair in new[] { ("rider1", a), ("rider2", b) })
            if (pair.Item2.GetProperty("status").GetString() == "AwaitingDriverAcceptance")
                await Act(pair.Item1, pair.Item2.GetProperty("id").GetGuid(), "cancel", pair.Item2);
        await Online();
    }

    private static async Task Online()
    {
        for (var i = 1; i <= 4; i++)
        {
            await Send(
                $"driver{i}",
                "PUT",
                "/drivers/me/location",
                new LocationRequest(Pickup.Latitude + i * 0.0001, Pickup.Longitude)
            );
            await Send(
                $"driver{i}",
                "PUT",
                "/drivers/me/availability",
                new AvailabilityRequest(true)
            );
        }
    }

    private static string DriverUser(JsonElement trip)
    {
        var id = trip.GetProperty("driverId").GetString()!;
        return "driver" + int.Parse(id[^12..]);
    }

    private static Task<JsonElement> Confirm(Guid id, JsonElement trip, string user = "rider1") =>
        Send(
            user,
            "POST",
            $"/trips/{id}/confirm-quote",
            new
            {
                expectedVersion = trip.GetProperty("version").GetInt64(),
                quoteId = trip.GetProperty("quote").GetProperty("id").GetGuid(),
            }
        );

    private static Task<JsonElement> Act(string user, Guid id, string action, JsonElement trip) =>
        Send(
            user,
            "POST",
            $"/trips/{id}/{action}",
            new VersionRequest(trip.GetProperty("version").GetInt64())
        );

    private static async Task Complete(Guid id, JsonElement trip, string final = "Paid")
    {
        await Confirm(id, trip);
        trip = await Wait(id, "AwaitingDriverAcceptance");
        var driver = DriverUser(trip);
        await Act(driver, id, "driver-accept", trip);
        trip = await Wait(id, "DriverAccepted");
        trip = await Act(driver, id, "en-route", trip);
        trip = await Act(driver, id, "pickup", trip);
        await Act(driver, id, "dropoff", trip);
        await Wait(id, final);
    }

    private static async Task CheckLedger(Guid id, int expected)
    {
        var ledger = await Send("rider1", "GET", $"/payments/{id}/ledger");
        var rows = JsonSerializer.Deserialize<List<JournalView>>(ledger, Json)!;
        Assert(rows.Count == expected, $"ledger journal count {expected}");
        foreach (var journal in rows)
            Ledger.Validate(journal.Lines);
    }

    private sealed record JournalView(List<LedgerLine> Lines);

    private static async Task<JsonElement> Wait(Guid id, string status, string user = "rider1")
    {
        var end = DateTimeOffset.UtcNow.AddSeconds(90);
        JsonElement trip = default;
        do
        {
            trip = await Send(user, "GET", $"/trips/{id}");
            if (trip.GetProperty("status").GetString() == status)
                return trip;
            await Task.Delay(1500);
        } while (DateTimeOffset.UtcNow < end);
        throw new Exception(
            $"Timeout waiting for {status}; actual={trip.GetProperty("status").GetString()}"
        );
    }

    private static async Task<JsonElement> Send(
        string user,
        string method,
        string path,
        object? body = null,
        string? key = null
    )
    {
        using var response = await Authorized(user, method, path, body, key);
        if (!response.IsSuccessStatusCode)
            throw new Exception(
                $"{method} {path} {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}"
            );
        if (response.StatusCode == HttpStatusCode.NoContent)
            return default;
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> Authorized(
        string user,
        string method,
        string path,
        object? body,
        string? key
    )
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = Request(user, method, path, body, key);
            var response = await Http.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.Unauthorized || attempt > 0)
                return response;
            response.Dispose();
            Tokens[user] = await Token(user);
        }
    }

    private static HttpRequestMessage Request(
        string user,
        string method,
        string path,
        object? body,
        string? key
    )
    {
        var request = new HttpRequestMessage(new HttpMethod(method), Base + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Tokens[user]);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);
        if (key is not null)
            request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static async Task Expect(
        string user,
        string method,
        string path,
        object? body,
        int status,
        string? key = null
    )
    {
        using var response = await Authorized(user, method, path, body, key);
        Assert((int)response.StatusCode == status, $"{method} {path} returns {status}");
    }

    private static async Task<string> Token(string user)
    {
        var name =
            user.StartsWith("driver") ? "DRIVER_PASSWORD"
            : user.StartsWith("ops") ? "OPS_PASSWORD"
            : "RIDER_PASSWORD";
        using var response = await Http.PostAsync(
            (Environment.GetEnvironmentVariable("IDENTITY_URL") ?? "http://localhost:8081")
                + "/realms/swiftride/protocol/openid-connect/token",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = "swiftride-check",
                    ["username"] = user,
                    ["password"] =
                        Environment.GetEnvironmentVariable(name)
                        ?? throw new Exception($"Missing {name}; run scripts/verify.sh"),
                }
            )
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("access_token")
            .GetString()!;
    }
}
