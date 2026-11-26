using SwiftRide.SharedKernel;

namespace SwiftRide.Trips.Domain;

public sealed class Trip
{
    public Guid Id { get; set; }
    public string RiderId { get; set; } = "";
    public Point Pickup { get; set; } = new(0, 0, "");
    public Point Destination { get; set; } = new(0, 0, "");
    public TripStatus Status { get; private set; } = TripStatus.Requested;
    public string? DriverId { get; private set; }
    public Guid? ReservationId { get; private set; }
    public Quote? Quote { get; private set; }
    public PaymentMethod Method { get; set; }
    public string? PaymentToken { get; set; }
    public int Rejections { get; private set; }
    public bool Refunded { get; private set; }
    public long PaymentAttempt { get; private set; }
    public IReadOnlyList<string> ExcludedDrivers { get; private set; } = [];
    public DateTimeOffset? Deadline { get; private set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public string? Reason { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyList<TripChange> History { get; private set; } =
    [new(TripStatus.Requested, DateTimeOffset.UtcNow, null)];
    public string RequestHash { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public int WorkflowRetries { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }

    public void Move(TripStatus next, string? reason = null)
    {
        TripState.For(Status).Ensure(next);
        if (next == TripStatus.PaymentPending)
            PaymentAttempt++;
        Status = next;
        Reason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
        Version++;
        History = [.. History, new(next, UpdatedAt, reason)];
    }

    public void RecordRefund()
    {
        if (
            Status is not (TripStatus.PaymentPending or TripStatus.PaymentReview or TripStatus.Paid)
        )
            throw new RuleException("Trip has no completed ride.");
        if (Status != TripStatus.PaymentReview)
            Move(TripStatus.PaymentReview, "Payment refunded.");
        else
        {
            Reason = "Payment refunded.";
            UpdatedAt = DateTimeOffset.UtcNow;
            History = [.. History, new(Status, UpdatedAt, Reason)];
            Version++;
        }
        Refunded = true;
    }

    public void CheckVersion(long expected)
    {
        if (Version != expected)
            throw new RuleException("Chuyến vừa thay đổi. Hãy tải lại.");
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
        if (Status != TripStatus.AwaitingDriverAcceptance)
            throw new RuleException("Offer is no longer active.");
        Rejections++;
        DriverUnavailable("Searching for another driver.", k);
    }

    public void DriverUnavailable(string reason, int k = 3)
    {
        if (DriverId is not null)
            ExcludedDrivers = [.. ExcludedDrivers, DriverId];
        var exhausted = Rejections > k;
        Move(
            exhausted ? TripStatus.Failed : TripStatus.Requested,
            exhausted ? "Driver rejection limit reached." : reason
        );
        DriverId = null;
        ReservationId = null;
        Quote = null;
        Deadline = null;
    }

    public void OfferQuote(string driverId, Quote quote)
    {
        Move(TripStatus.AwaitingRiderConfirmation);
        DriverId = driverId;
        Quote = quote;
        Deadline = quote.ExpiresAt;
    }

    public void OfferDriver(DateTimeOffset expiresAt)
    {
        Move(TripStatus.AwaitingDriverAcceptance);
        Deadline = expiresAt;
    }

    public void AcceptDriver()
    {
        Move(TripStatus.DriverAccepted);
        Deadline = null;
    }

    public void ScheduleRetry(DateTimeOffset now, double jitter)
    {
        WorkflowRetries++;
        NextAttemptAt = now.AddSeconds(Math.Pow(2, WorkflowRetries) + jitter);
        Reason = "Dịch vụ tạm thời lỗi. Đang thử lại.";
        Version++;
        UpdatedAt = now;
    }

    public void ClearRetry()
    {
        WorkflowRetries = 0;
        NextAttemptAt = null;
    }
}
