using SwiftRide.SharedKernel;

namespace SwiftRide.Trips.Domain;

public sealed class Trip
{
    public Guid Id { get; set; }
    public string RiderId { get; set; } = "";
    public Point Pickup { get; set; } = new(0, 0, "");
    public Point Destination { get; set; } = new(0, 0, "");
    public string RiderName { get; set; } = "";
    public string RiderPhone { get; set; } = "";
    public string PickupNote { get; set; } = "";
    public int? Rating { get; private set; }
    public string? Review { get; private set; }
    public DateTimeOffset? RatedAt { get; private set; }
    public TripStatus Status { get; private set; } = TripStatus.Requested;
    public string? DriverId { get; private set; }
    public Guid? ReservationId { get; private set; }
    public Quote? Quote { get; private set; }
    public PaymentMethod Method { get; set; }
    public string? PaymentToken { get; set; }
    public int Rejections { get; private set; }
    public bool CanCancelWalletHold =>
        Method == PaymentMethod.Wallet
        && Status == TripStatus.PaymentReview
        && !History.Any(change =>
            change.Status
                is TripStatus.DriverAccepted
                    or TripStatus.EnRoute
                    or TripStatus.PickedUp
                    or TripStatus.DroppedOff
                    or TripStatus.PaymentPending
                    or TripStatus.Paid
        );
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
        Status = next;
        Reason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
        Version++;
        History = [.. History, new(next, UpdatedAt, reason)];
    }

    public void CheckVersion(long expected)
    {
        if (Version != expected)
            throw new RuleException("Chuyến vừa thay đổi. Hãy tải lại.");
    }

    public void Rate(int rating, string? comment, long expectedVersion)
    {
        var text = comment?.Trim() ?? "";
        if (rating is < 1 or > 5 || text.Length > 500)
            throw new RuleException(
                "Đánh giá cần 1–5 sao, nhận xét tối đa 500 ký tự.",
                ErrorKind.Validation
            );
        if (
            Status is not (TripStatus.PaymentPending or TripStatus.PaymentReview or TripStatus.Paid)
            || !History.Any(change => change.Status == TripStatus.DroppedOff)
        )
            throw new RuleException("Chỉ đánh giá sau khi đã trả khách.");
        if (Rating is not null)
        {
            if (Rating == rating && Review == text)
                return;
            throw new RuleException("Chuyến đã được đánh giá.");
        }
        CheckVersion(expectedVersion);
        Rating = rating;
        Review = text;
        RatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = RatedAt.Value;
        Version++;
    }

    public void ConfirmQuote(Guid quoteId, DateTimeOffset now)
    {
        if (Quote is null || Quote.Id != quoteId || Quote.ExpiresAt <= now)
            throw new RuleException("Báo giá đã hết hạn hoặc thay đổi.");
        Move(TripStatus.ReservingDriver);
        ReservationId = Guid.NewGuid();
    }

    public void BeginWalletHold(Guid quoteId, DateTimeOffset now)
    {
        if (Method != PaymentMethod.Wallet || Status != TripStatus.AwaitingRiderConfirmation)
            throw new RuleException("Chuyến không chờ giữ tiền ví.");
        if (Quote is null || Quote.Id != quoteId || Quote.ExpiresAt <= now)
            throw new RuleException("Báo giá đã hết hạn hoặc thay đổi.");
        Move(TripStatus.WalletHoldPending);
    }

    public void WalletHoldSucceeded(Guid quoteId, DateTimeOffset now)
    {
        if (
            Status != TripStatus.WalletHoldPending
            || Quote?.Id != quoteId
            || Quote.ExpiresAt <= now
        )
            throw new RuleException("Báo giá không còn hiệu lực để giữ tiền.");
        Move(TripStatus.ReservingDriver);
        ReservationId = Guid.NewGuid();
    }

    public void WalletHoldFailed(string reason)
    {
        if (Status == TripStatus.WalletHoldPending)
            Move(TripStatus.PaymentReview, reason);
    }

    public void WalletHoldReleased()
    {
        if (Status != TripStatus.PaymentReview)
            return;
        // The closed quote must never be reused by a delayed hold request or a new confirmation.
        if (Quote is not null)
            Quote = Quote with { ExpiresAt = DateTimeOffset.UtcNow };
        Deadline = DateTimeOffset.UtcNow;
        Move(
            TripStatus.AwaitingRiderConfirmation,
            "Wallet hold released. Add funds if needed, then confirm the quote again."
        );
    }

    public void WalletHoldReleasedForRematch()
    {
        if (Status != TripStatus.PaymentReview)
            return;
        var exhausted = Rejections > 3;
        DriverId = null;
        ReservationId = null;
        Quote = null;
        Deadline = null;
        Move(
            exhausted ? TripStatus.Failed : TripStatus.Requested,
            exhausted
                ? Reason ?? "Driver search attempts exhausted."
                : "Wallet hold released. Searching for a new driver and quote."
        );
    }

    public void WalletHoldCancelled() =>
        Move(TripStatus.Cancelled, "Rider cancelled the trip after the wallet hold was released.");

    public void RejectDriver(int k = 3)
    {
        if (Status != TripStatus.AwaitingDriverAcceptance)
            throw new RuleException("Offer is no longer active.");
        if (DriverId is not null)
            ExcludeDriver();
        Rejections++;
        var exhausted = Rejections > k;
        var next =
            Method == PaymentMethod.Wallet ? TripStatus.PaymentReview
            : exhausted ? TripStatus.Failed
            : TripStatus.Requested;
        Move(next, exhausted ? "Driver rejection limit reached." : "Searching for another driver.");
        Deadline = null;
    }

    public void DriverUnavailable(string reason)
    {
        if (DriverId is not null)
            ExcludeDriver();
        Rejections++;
        var exhausted = Rejections > 3;
        var next =
            Method == PaymentMethod.Wallet ? TripStatus.PaymentReview
            : exhausted ? TripStatus.Failed
            : TripStatus.Requested;
        Move(next, reason);
        Deadline = null;
    }

    public void FailAfterWalletRelease(string reason)
    {
        Rejections = 4;
        Move(TripStatus.PaymentReview, reason);
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

    public void ExcludeDriver()
    {
        if (DriverId is not null)
            ExcludedDrivers = [.. ExcludedDrivers, DriverId];
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
