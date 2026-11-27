using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SwiftRide.Infrastructure;

public static class Telemetry
{
    public static readonly ActivitySource Activities = new("SwiftRide");
    public static readonly Meter Meter = new("SwiftRide");
    public static readonly Counter<long> Trips = Meter.CreateCounter<long>(
        "swiftride.trips.requested"
    );
    public static readonly Counter<long> Outcomes = Meter.CreateCounter<long>(
        "swiftride.trips.outcomes"
    );
    public static readonly Counter<long> PaymentAttempts = Meter.CreateCounter<long>(
        "swiftride.payment.attempts"
    );
    public static readonly Histogram<double> Matching = Meter.CreateHistogram<double>(
        "swiftride.matching.duration",
        "s"
    );
}
