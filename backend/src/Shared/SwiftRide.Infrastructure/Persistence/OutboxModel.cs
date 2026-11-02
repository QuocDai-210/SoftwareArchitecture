using System.Diagnostics;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace SwiftRide.Infrastructure;

public static class OutboxModel
{
    public static void Outbox(this ModelBuilder model)
    {
        model.AddInboxStateEntity();
        model.AddOutboxMessageEntity();
        model.AddOutboxStateEntity();
    }
}
