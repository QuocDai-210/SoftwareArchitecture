using SwiftRide.SharedKernel;

namespace SwiftRide.Matching.Domain;

public sealed class Simulation
{
    public string DriverId { get; init; } = "";
    public Guid TripId { get; init; }
    public Guid ReservationId { get; init; }
    public long TripVersion { get; init; }
    public string Stage { get; init; } = "";
    public RoadRoute Route { get; init; } = new(1, 1, []);
    public double Progress { get; private set; }
    public int Speed { get; private set; } = 10;
    public bool Paused { get; private set; }
    public long Revision { get; private set; }
    public bool Arrived => Progress >= 1;
    public Point CurrentPosition => Position();
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void StartAfter(long revision) => Revision = checked(revision + 1);

    public void Control(int speed, bool paused)
    {
        if (speed is not (1 or 5 or 10 or 20))
            throw new RuleException("Tốc độ phải là 1, 5, 10 hoặc 20.", ErrorKind.Validation);
        Speed = speed;
        Paused = paused;
        Revision++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Advance(double seconds, DateTimeOffset now)
    {
        if (!Paused && seconds > 0)
            Progress = Math.Min(
                1,
                Progress + Math.Min(seconds, 2) * Speed / ((double)Route.DurationMinutes * 60)
            );
        Revision++;
        UpdatedAt = now;
    }

    public Point Position()
    {
        var coordinates = Route.Coordinates;
        if (coordinates.Length < 2)
            throw new RuleException("Thiếu tuyến đường giả lập.");
        var lengths = coordinates
            .Zip(coordinates.Skip(1), (a, b) => GeoDistance.Kilometers(a[1], a[0], b[1], b[0]))
            .ToArray();
        var remaining = lengths.Sum() * Progress;
        for (var i = 0; i < lengths.Length; i++)
        {
            if (remaining <= lengths[i] && lengths[i] > 0)
            {
                var fraction = remaining / lengths[i];
                return new(
                    coordinates[i][1] + (coordinates[i + 1][1] - coordinates[i][1]) * fraction,
                    coordinates[i][0] + (coordinates[i + 1][0] - coordinates[i][0]) * fraction,
                    "Xe giả lập"
                );
            }
            remaining -= lengths[i];
        }
        return new(coordinates[^1][1], coordinates[^1][0], "Xe giả lập");
    }
}
