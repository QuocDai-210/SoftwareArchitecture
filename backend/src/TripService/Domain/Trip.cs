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

    public void RejectDriver(int k = 3)
    {
        if (Status != TripStatus.AwaitingDriverAcceptance)
            throw new RuleException("Lời mời không còn hiệu lực.");
        if (DriverId is not null)
            ExcludeDriver();
        Rejections++;
        Move(
            Rejections > k ? TripStatus.Failed : TripStatus.Requested,
            Rejections > k ? "Tài xế từ chối quá giới hạn." : "Đang tìm tài xế khác."
        );
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
