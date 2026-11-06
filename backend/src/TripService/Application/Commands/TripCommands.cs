using System.Text.Json;
using SwiftRide.Trips.Application.Ports;

namespace SwiftRide.Trips.Application;

public sealed record CreatedTrip(TripView View, Guid Id, bool IsNew);

public sealed class TripCommands(
    ITripReader reader,
    ITripWriter writer,
    IEventPublisher bus,
    ITripMetrics metrics,
    IMatchingClient matching,
    IUserProfileStore profiles
)
{
    public async Task<CreatedTrip> Create(
        CreateTripRequest request,
        string key,
        Actor actor,
        string correlation,
        CancellationToken ct
    )
    {
        if (request.Pickup is null || request.Destination is null)
            throw new RuleException("Cần điểm đón và điểm đến.", ErrorKind.Validation);
        request.Pickup.Validate();
        request.Destination.Validate();
        if (
            request.Pickup.Latitude == request.Destination.Latitude
            && request.Pickup.Longitude == request.Destination.Longitude
        )
            throw new RuleException("Điểm đón và điểm đến phải khác nhau.", ErrorKind.Validation);
        if (!Enum.IsDefined(request.Method))
            throw new RuleException("Phương thức thanh toán không hợp lệ.", ErrorKind.Validation);
        if (request.Method == PaymentMethod.Card || request.PaymentToken is not null)
            throw new RuleException(
                "Chuyến mới chỉ hỗ trợ thanh toán bằng ví hoặc tiền mặt.",
                ErrorKind.Validation
            );
        if ((request.PickupNote?.Length ?? 0) > 240)
            throw new RuleException("Ghi chú đón khách tối đa 240 ký tự.", ErrorKind.Validation);
        RequestKeys.Validate(key);
        if (!actor.IsRider)
            throw new RuleException("Cần quyền Rider.", ErrorKind.Forbidden);
        var hash = RequestKeys.Hash(JsonSerializer.Serialize(request, ApplicationJson.Options));
        var rider = actor.Id;
        var previous = await reader.ByBookingKey(rider, key, ct);
        if (previous is not null)
        {
            RequestKeys.Same(previous.RequestHash, hash);
            return new(TripView.From(previous), previous.Id, false);
        }
        var profile = await profiles.Get(rider, ct);
        if (
            profile is null
            || string.IsNullOrWhiteSpace(profile.Name)
            || string.IsNullOrWhiteSpace(profile.Phone)
        )
            throw new RuleException(
                "Bổ sung họ tên và số điện thoại liên hệ trước khi đặt xe.",
                ErrorKind.Validation
            );
        if (
            (await reader.List(rider, false, ct)).Any(x =>
                x.Status
                    is TripStatus.AwaitingRiderConfirmation
                        or TripStatus.WalletHoldPending
                        or TripStatus.ReservingDriver
                        or TripStatus.AwaitingDriverAcceptance
                        or TripStatus.AcceptingDriver
                        or TripStatus.DriverAccepted
                        or TripStatus.EnRoute
                        or TripStatus.PickedUp
                        or TripStatus.PaymentPending
                        or TripStatus.PaymentReview
            )
        )
            throw new RuleException(
                "Bạn còn chuyến đang chờ thanh toán. Hoàn tất trước khi đặt chuyến mới."
            );
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RiderId = rider,
            Pickup = request.Pickup,
            Destination = request.Destination,
            RiderName = profile.Name,
            RiderPhone = profile.Phone,
            PickupNote = request.PickupNote?.Trim() ?? "",
            Method = request.Method,
            PaymentToken = request.PaymentToken,
            IdempotencyKey = key!,
            RequestHash = hash,
            CorrelationId = correlation,
        };
        var stored = await writer.Create(trip, ct);
        RequestKeys.Same(stored.RequestHash, hash);
        var created = stored.Id == trip.Id;
        if (created)
            metrics.Requested();
        return new(TripView.From(stored), stored.Id, created);
    }

    public async Task<TripView> ConfirmQuote(
        Guid id,
        ConfirmQuoteRequest request,
        Actor actor,
        CancellationToken ct
    )
    {
        var trip = await reader.Get(id, ct);
        TripAccess.Rider(trip, actor);
        trip.CheckVersion(request.ExpectedVersion);
        if (trip.Method == PaymentMethod.Wallet)
        {
            trip.BeginWalletHold(request.QuoteId, DateTimeOffset.UtcNow);
            await writer.Save(ct);
            await bus.Publish(WorkflowEvents.ReserveWallet(trip), trip.CorrelationId, ct);
            return TripView.From(trip);
        }
        trip.ConfirmQuote(request.QuoteId, DateTimeOffset.UtcNow);
        await writer.Save(ct);
        return TripView.From(trip);
    }

    public async Task<TripView> Cancel(
        Guid id,
        CancelTripRequest request,
        Actor actor,
        CancellationToken ct
    )
    {
        var trip = await reader.Get(id, ct);
        if (actor.IsRider && trip.Status == TripStatus.WalletHoldPending)
        {
            throw new RuleException(
                "Đang xác nhận khoản giữ ví. Vui lòng đợi kết quả rồi thử lại."
            );
        }
        if (actor.IsRider)
            TripAccess.Rider(trip, actor);
        else
        {
            TripAccess.Driver(trip, actor);
            if (trip.Status is not (TripStatus.DriverAccepted or TripStatus.EnRoute))
                throw new RuleException(
                    "Chỉ hủy trước khi đón khách; lời mời chưa nhận dùng Từ chối."
                );
        }
        var reason = request.Reason?.Trim();
        if (
            (reason?.Length ?? 0) > 240
            || (
                trip.Status is TripStatus.DriverAccepted or TripStatus.EnRoute
                && string.IsNullOrWhiteSpace(reason)
            )
        )
            throw new RuleException("Cần lý do hủy, tối đa 240 ký tự.", ErrorKind.Validation);
        trip.CheckVersion(request.ExpectedVersion);
        trip.Move(
            TripStatus.Cancelled,
            $"{(actor.IsRider ? "Người đặt xe" : "Tài xế")} hủy chuyến. {reason}".Trim()
        );
        if (trip.Method == PaymentMethod.Wallet)
            await bus.Publish(WorkflowEvents.ReleaseWallet(trip), trip.CorrelationId, ct);
        await WorkflowEvents.Release(trip, bus, ct);
        await writer.Save(ct);
        metrics.Outcome("cancelled");
        return TripView.From(trip);
    }

    public async Task<TripView> Review(
        Guid id,
        ReviewTripRequest request,
        Actor actor,
        CancellationToken ct
    )
    {
        var trip = await reader.Get(id, ct);
        TripAccess.Rider(trip, actor);
        trip.Rate(request.Rating, request.Comment, request.ExpectedVersion);
        await writer.Save(ct);
        return TripView.From(trip);
    }

    public async Task<TripView> RejectDriver(
        Guid id,
        VersionRequest request,
        Actor actor,
        CancellationToken ct
    )
    {
        var trip = await reader.Get(id, ct);
        TripAccess.Driver(trip, actor);
        trip.CheckVersion(request.ExpectedVersion);
        trip.RejectDriver();
        if (trip.Method == PaymentMethod.Wallet)
            await bus.Publish(WorkflowEvents.ReleaseWallet(trip), trip.CorrelationId, ct);
        await WorkflowEvents.Release(trip, bus, ct);
        await writer.Save(ct);
        return TripView.From(trip);
    }

    public async Task<TripView> AcceptDriver(
        Guid id,
        VersionRequest request,
        Actor actor,
        CancellationToken ct
    )
    {
        var trip = await reader.Get(id, ct);
        TripAccess.Driver(trip, actor);
        trip.CheckVersion(request.ExpectedVersion);
        if (trip.Deadline <= DateTimeOffset.UtcNow)
            throw new RuleException("Lời mời đã hết hạn.");
        trip.Move(TripStatus.AcceptingDriver);
        await writer.Save(ct);
        return TripView.From(trip);
    }

    public async Task<TripView> RetryPayment(
        Guid id,
        VersionRequest request,
        Actor actor,
        CancellationToken ct
    )
    {
        if (!actor.IsOps)
            throw new RuleException("Cần quyền Ops.", ErrorKind.Forbidden);
        var trip = await reader.Get(id, ct);
        if (trip.Method != PaymentMethod.Cod)
            throw new RuleException("Giao dịch online cũ chỉ đọc; cần đối soát ngoài hệ thống.");
        trip.CheckVersion(request.ExpectedVersion);
        trip.Move(TripStatus.PaymentPending);
        await bus.Publish(WorkflowEvents.Completed(trip), trip.CorrelationId, ct);
        await writer.Save(ct);
        return TripView.From(trip);
    }

    public async Task<TripView> Move(
        Guid id,
        VersionRequest request,
        Actor actor,
        TripStatus target,
        CancellationToken ct
    )
    {
        if (target is not (TripStatus.EnRoute or TripStatus.PickedUp or TripStatus.DroppedOff))
            throw new RuleException("Thao tác hành trình không hợp lệ.", ErrorKind.Validation);
        var trip = await reader.Get(id, ct);
        TripAccess.Driver(trip, actor);
        trip.CheckVersion(request.ExpectedVersion);
        if (target is TripStatus.PickedUp or TripStatus.DroppedOff)
            await matching.EnsureArrival(
                new(trip.DriverId!, trip.Id, target.ToString()),
                trip.CorrelationId,
                ct
            );
        trip.Move(target);
        if (target is TripStatus.EnRoute or TripStatus.PickedUp)
            await bus.Publish(
                new TripProgressed(
                    Guid.NewGuid(),
                    trip.Id,
                    trip.DriverId!,
                    trip.ReservationId!.Value,
                    trip.Version,
                    target.ToString(),
                    trip.Pickup,
                    trip.Destination,
                    trip.Quote?.Route,
                    trip.CorrelationId
                ),
                trip.CorrelationId,
                ct
            );
        if (target == TripStatus.DroppedOff)
        {
            trip.Move(TripStatus.PaymentPending);
            await bus.Publish(WorkflowEvents.Completed(trip), trip.CorrelationId, ct);
        }
        await writer.Save(ct);
        return TripView.From(trip);
    }
}
