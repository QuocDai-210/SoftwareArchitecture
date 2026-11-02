using System.Text.Json.Serialization;

namespace SwiftRide.Contracts;

public sealed record AvailabilityRequest([property: JsonRequired] bool Available);
