using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace SwiftRide.Core;

public sealed class RuleException(string message, int status = 409) : Exception(message)
{
    public int Status { get; } = status;
}

public enum TripStatus
{
    Requested, AwaitingRiderConfirmation, ReservingDriver, AwaitingDriverAcceptance,
    AcceptingDriver, DriverAccepted, EnRoute, PickedUp, DroppedOff, PaymentPending,
    PaymentReview, Paid, Cancelled, Failed
}
public enum PaymentStatus { Pending, Settled, Declined, Refunded, Compensated }
public enum PaymentMethod { Card, Wallet }

public sealed record Point([property: JsonRequired] double Latitude, [property: JsonRequired] double Longitude, [property: JsonRequired] string Label)
{
    public void Validate()
    {
        if (!double.IsFinite(Latitude) || !double.IsFinite(Longitude) ||
            Latitude is < -90 or > 90 || Longitude is < -180 or > 180 ||
            string.IsNullOrWhiteSpace(Label) || Label.Length > 120)
            throw new RuleException("Điểm đón/đến không hợp lệ.", 400);
    }
}
public sealed record RouteEstimate(decimal DistanceKm, decimal DurationMinutes);
public static class Routes
{
    public static RouteEstimate Estimate(Point from, Point to)
    {
        if (from is null || to is null) throw new RuleException("Cần điểm đón và điểm đến.", 400);
        from.Validate(); to.Validate();
        var lat = (to.Latitude - from.Latitude) * Math.PI / 180;
        var lon = (to.Longitude - from.Longitude) * Math.PI / 180;
        var a = Math.Pow(Math.Sin(lat / 2), 2) + Math.Cos(from.Latitude * Math.PI / 180) *
            Math.Cos(to.Latitude * Math.PI / 180) * Math.Pow(Math.Sin(lon / 2), 2);
        var distance = (decimal)(6371 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)))) * 1.3m;
        if (distance < 0.05m || distance > 100m)
            throw new RuleException("Demo hỗ trợ tuyến từ 50 m đến 100 km.", 400);
        // ponytail: tuyến demo, thay bằng route provider khi cần đường đi thực tế.
        return new(decimal.Round(distance, 3), decimal.Round(distance / 25 * 60, 2));
    }
}
public sealed record FareInput(decimal DistanceKm, decimal DurationMinutes, decimal PerKm = 10000,
    decimal PerMinute = 500, decimal Surge = 1, decimal Toll = 0, decimal Promotion = 1, decimal Tax = 0.1m)
{
    public void Validate()
    {
        if (DistanceKm is <= 0 or > 100 || DurationMinutes is <= 0 or > 1440 ||
            PerKm is < 0 or > 1000000 || PerMinute is < 0 or > 100000 || Surge is < 1 or > 5 ||
            Toll is < 0 or > 10000000 || Promotion is < 0 or > 1 || Tax is < 0 or > 1)
            throw new RuleException("Thông số tính giá không hợp lệ.", 400);
    }
}
public interface IPricingStrategy { decimal Calculate(FareInput input); }
public sealed class StandardPricing : IPricingStrategy
{
    public decimal Calculate(FareInput f) { f.Validate(); return f.PerKm * f.DistanceKm + f.PerMinute * f.DurationMinutes; }
}
public sealed class SurgePricing(IPricingStrategy basis) : IPricingStrategy
{
    public decimal Calculate(FareInput f) { f.Validate(); return basis.Calculate(f) * f.Surge; }
}
public sealed class PromoPricing(IPricingStrategy basis) : IPricingStrategy
{
    public decimal Calculate(FareInput f) { f.Validate(); return (basis.Calculate(f) + f.Toll) * f.Promotion; }
}
public sealed class FareCalculator(IPricingStrategy strategy)
{
    public decimal Calculate(FareInput input)
    {
        input.Validate();
        var value = strategy.Calculate(input) * (1 + input.Tax);
        if (value < 0) throw new RuleException("Giá không được âm.");
        return decimal.Round(value, 0, MidpointRounding.AwayFromZero);
    }
}
public sealed record Quote(Guid Id, decimal Amount, string Currency, FareInput Breakdown, DateTimeOffset ExpiresAt);

public abstract class TripState
{
    public abstract IReadOnlySet<TripStatus> Next { get; }
    public void Ensure(TripStatus target)
    {
        if (!Next.Contains(target)) throw new RuleException($"Không thể chuyển sang {target}.");
    }
    private sealed class Allowed(params TripStatus[] next) : TripState
    {
        public override IReadOnlySet<TripStatus> Next { get; } = next.ToHashSet();
    }
    private static readonly IReadOnlyDictionary<TripStatus, TripState> States = new Dictionary<TripStatus, TripState>
    {
        [TripStatus.Requested] = new Allowed(TripStatus.AwaitingRiderConfirmation, TripStatus.Cancelled, TripStatus.Failed),
        [TripStatus.AwaitingRiderConfirmation] = new Allowed(TripStatus.ReservingDriver, TripStatus.Cancelled, TripStatus.Requested),
        [TripStatus.ReservingDriver] = new Allowed(TripStatus.AwaitingDriverAcceptance, TripStatus.Requested, TripStatus.Failed),
        [TripStatus.AwaitingDriverAcceptance] = new Allowed(TripStatus.AcceptingDriver, TripStatus.Requested, TripStatus.Cancelled, TripStatus.Failed),
        [TripStatus.AcceptingDriver] = new Allowed(TripStatus.DriverAccepted, TripStatus.Requested, TripStatus.Failed),
        [TripStatus.DriverAccepted] = new Allowed(TripStatus.EnRoute),
        [TripStatus.EnRoute] = new Allowed(TripStatus.PickedUp),
        [TripStatus.PickedUp] = new Allowed(TripStatus.DroppedOff),
        [TripStatus.DroppedOff] = new Allowed(TripStatus.PaymentPending),
        [TripStatus.PaymentPending] = new Allowed(TripStatus.Paid, TripStatus.PaymentReview),
        [TripStatus.PaymentReview] = new Allowed(TripStatus.PaymentPending, TripStatus.Paid),
        [TripStatus.Paid] = new Allowed(), [TripStatus.Cancelled] = new Allowed(), [TripStatus.Failed] = new Allowed()
    };
    public static TripState For(TripStatus status) => States[status];
}
public sealed class Trip
{
    public Guid Id { get; set; }
    public string RiderId { get; set; } = "";
    public Point Pickup { get; set; } = new(0, 0, "");
    public Point Destination { get; set; } = new(0, 0, "");
    public TripStatus Status { get; set; } = TripStatus.Requested;
    public string? DriverId { get; set; }
    public Guid? ReservationId { get; set; }
    public Quote? Quote { get; set; }
    public PaymentMethod Method { get; set; }
    public string? PaymentToken { get; set; }
    public int Rejections { get; set; }
    public List<string> ExcludedDrivers { get; set; } = [];
    public DateTimeOffset? Deadline { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Reason { get; set; }
    public long Version { get; set; }
    public List<TripChange> History { get; set; } = [];
    public string RequestHash { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public int WorkflowRetries { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }

    public void Move(TripStatus next, string? reason = null)
    {
        TripState.For(Status).Ensure(next);
        Status = next; Reason = reason; UpdatedAt = DateTimeOffset.UtcNow; Version++;
        History.Add(new(next, UpdatedAt, reason));
    }
    public void CheckVersion(long expected)
    {
        if (Version != expected) throw new RuleException("Chuyến vừa thay đổi. Hãy tải lại.");
    }
    public void ConfirmQuote(Guid quoteId, DateTimeOffset now)
    {
        if (Quote is null || Quote.Id != quoteId || Quote.ExpiresAt <= now)
            throw new RuleException("Báo giá đã hết hạn hoặc thay đổi.");
        Move(TripStatus.ReservingDriver);
        ReservationId = Guid.NewGuid();
    }
    public void RejectDriver(int k = 3)
    {
        if (Status != TripStatus.AwaitingDriverAcceptance) throw new RuleException("Lời mời không còn hiệu lực.");
        if (DriverId is not null) ExcludedDrivers.Add(DriverId);
        Rejections++;
        Move(Rejections > k ? TripStatus.Failed : TripStatus.Requested,
            Rejections > k ? "Tài xế từ chối quá giới hạn." : "Đang tìm tài xế khác.");
        Deadline = null;
    }
}
public sealed record TripChange(TripStatus Status, DateTimeOffset At, string? Reason);
public static class RequestKeys
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static void Same(string stored, string incoming)
    {
        if (stored != incoming) throw new RuleException("Idempotency-Key đã được dùng với nội dung khác.");
    }
    public static void Validate(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || key.Any(char.IsControl))
            throw new RuleException("Cần Idempotency-Key hợp lệ, tối đa 100 ký tự.", 400);
    }
}
public enum EntrySide { Debit, Credit }
public sealed record LedgerLine(string Account, EntrySide Side, decimal Amount);
public static class Ledger
{
    public static void Validate(IReadOnlyCollection<LedgerLine> lines)
    {
        if (lines.Count < 2 || lines.Any(x => x.Amount < 0 || x.Amount != decimal.Round(x.Amount, 0) || !Enum.IsDefined(x.Side) || string.IsNullOrWhiteSpace(x.Account)) ||
            lines.Sum(x => x.Side == EntrySide.Debit ? x.Amount : -x.Amount) != 0)
            throw new RuleException("Bút toán không cân bằng.");
    }
    public static List<LedgerLine> Charge(PaymentMethod method, string rider, decimal amount)
    {
        if (!Enum.IsDefined(method) || amount < 0 || amount != decimal.Round(amount, 0)) throw new RuleException("Số tiền VND không hợp lệ.", 400);
        List<LedgerLine> lines = [new(method == PaymentMethod.Card ? "ProviderReceivable" : $"WalletLiability:{rider}", EntrySide.Debit, amount),
            new("TripPayable", EntrySide.Credit, amount)];
        Validate(lines); return lines;
    }
    public static List<LedgerLine> Reverse(IEnumerable<LedgerLine> lines) => lines
        .Select(x => x with { Side = x.Side == EntrySide.Debit ? EntrySide.Credit : EntrySide.Debit }).ToList();
}
public sealed record MatchRequest(Point Pickup, string[] ExcludedDrivers);
public sealed record DriverMatch(string DriverId, string Name, double DistanceMeters);
public sealed record MatchResponse(DriverMatch? Driver);
public sealed record QuoteRequest(Point Pickup, Point Destination);
public sealed record ReservationRequest(Guid TripId, Guid ReservationId, string DriverId);
public sealed record ReservationResult(bool Acquired, DateTimeOffset? ExpiresAt = null);
public sealed record CreateTripRequest([property: JsonRequired] Point Pickup, [property: JsonRequired] Point Destination, [property: JsonRequired] PaymentMethod Method, string? PaymentToken);
public sealed record VersionRequest([property: JsonRequired] long ExpectedVersion);
public sealed record ConfirmQuoteRequest([property: JsonRequired] long ExpectedVersion, [property: JsonRequired] Guid QuoteId);
public sealed record RefundRequest(string Reason);
public sealed record LocationRequest([property: JsonRequired] double Latitude, [property: JsonRequired] double Longitude);
public sealed record AvailabilityRequest([property: JsonRequired] bool Available);
public sealed record PaymentRequest([property: JsonRequired] Guid TripId, [property: JsonRequired] string RiderId, [property: JsonRequired] decimal Amount, [property: JsonRequired] PaymentMethod Method, string? PaymentToken, string CorrelationId);
public sealed record TripAccepted(Guid EventId, Guid TripId, string DriverId, Guid ReservationId, string CorrelationId);
public sealed record ReservationReleased(Guid EventId, Guid TripId, string DriverId, Guid ReservationId, string CorrelationId);
public sealed record RideCompleted(Guid EventId, Guid TripId, string RiderId, decimal Amount, PaymentMethod Method, string? PaymentToken, string CorrelationId);
public sealed record PaymentSettled(Guid EventId, Guid TripId, Guid PaymentId, decimal Amount, string Currency, string CorrelationId);
public sealed record PaymentFailed(Guid EventId, Guid TripId, string Reason, bool Compensated, string CorrelationId);
public sealed record PaymentRefunded(Guid EventId, Guid TripId, Guid PaymentId, decimal Amount, string CorrelationId);
