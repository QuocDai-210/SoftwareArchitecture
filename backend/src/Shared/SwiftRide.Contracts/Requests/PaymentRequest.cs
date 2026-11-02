using System.Text.Json.Serialization;
using SwiftRide.SharedKernel;

namespace SwiftRide.Contracts;

public sealed record PaymentRequest(
    [property: JsonRequired] Guid TripId,
    [property: JsonRequired] string RiderId,
    [property: JsonRequired] decimal Amount,
    [property: JsonRequired] PaymentMethod Method,
    string? PaymentToken,
    string CorrelationId,
    long Attempt = 0,
    string? DriverId = null
);
