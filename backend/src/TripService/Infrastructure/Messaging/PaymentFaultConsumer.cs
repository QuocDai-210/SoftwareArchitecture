using MassTransit;

namespace SwiftRide.Trips.Infrastructure;

public sealed class PaymentFaultConsumer(TripPaymentNotifications notifications)
    : IConsumer<Fault<RideCompleted>>,
        IConsumer<Fault<WalletTripHoldRequested>>
{
    public Task Consume(ConsumeContext<Fault<RideCompleted>> context) =>
        notifications.Faulted(context.Message.Message, context.CancellationToken);

    public Task Consume(ConsumeContext<Fault<WalletTripHoldRequested>> context)
    {
        var hold = context.Message.Message;
        return notifications.WalletHoldFailed(
            new WalletTripHoldFailed(
                Guid.NewGuid(),
                hold.TripId,
                hold.RiderId,
                hold.Amount,
                "Giữ tiền ví đã hết lượt xử lý; đang giải phóng trước khi thử lại.",
                hold.CorrelationId,
                hold.QuoteId
            ),
            context.CancellationToken
        );
    }
}
