using System.Text.Json.Serialization;

namespace SwiftRide.Contracts;

public sealed record CancelTripRequest(
    [property: JsonRequired] long ExpectedVersion,
    string? Reason = null
);
