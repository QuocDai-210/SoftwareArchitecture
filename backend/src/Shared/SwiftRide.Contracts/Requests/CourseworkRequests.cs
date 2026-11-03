using System.Text.Json.Serialization;

namespace SwiftRide.Contracts;

public sealed record ApprovalRequest(string TemporaryPassword);

public sealed record RejectionRequest(string Reason);

public sealed record SuspensionRequest(bool Suspended);

public sealed record SimulationControlRequest(int Speed, bool Paused);

public sealed record ArrivalRequest(string DriverId, Guid TripId, string Target);

public sealed record TopUpRequest(decimal Amount);

public sealed record ProfileRequest(string Name, string Phone);

public sealed record CancelTripRequest(
    [property: JsonRequired] long ExpectedVersion,
    string? Reason = null
);

public sealed record ReviewTripRequest(
    [property: JsonRequired] long ExpectedVersion,
    [property: JsonRequired] int Rating,
    string? Comment = null
);

public sealed record PageResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total);
