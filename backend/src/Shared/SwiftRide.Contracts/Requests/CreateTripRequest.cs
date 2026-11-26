using System.Text.Json.Serialization;
using SwiftRide.SharedKernel;

namespace SwiftRide.Contracts;

public sealed record CreateTripRequest(
    [property: JsonRequired] Point Pickup,
    [property: JsonRequired] Point Destination,
    [property: JsonRequired] PaymentMethod Method,
    string? PaymentToken
);
