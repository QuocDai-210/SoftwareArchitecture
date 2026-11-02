using System.Text.Json.Serialization;

namespace SwiftRide.Contracts;

public sealed record VersionRequest([property: JsonRequired] long ExpectedVersion);
