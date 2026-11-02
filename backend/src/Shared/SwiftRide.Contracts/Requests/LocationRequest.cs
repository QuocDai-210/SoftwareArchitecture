using System.Text.Json.Serialization;

namespace SwiftRide.Contracts;

public sealed record LocationRequest(
    [property: JsonRequired] double Latitude,
    [property: JsonRequired] double Longitude
);
