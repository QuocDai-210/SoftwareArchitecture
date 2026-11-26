namespace SwiftRide.Trips.Infrastructure;

public sealed class TripRepository(TripDb db, ILogger<TripRepository> logger)
    : ITripReader,
        ITripWriter,
        ITripWorkflowQueue
{
    public async Task<Trip> Get(Guid id, CancellationToken ct) =>
        await db.Trips.SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new RuleException("Không tìm thấy chuyến.", ErrorKind.NotFound);

    public async Task<IReadOnlyList<Trip>> List(string subject, bool all, CancellationToken ct)
    {
        var query = db.Trips.AsNoTracking();
        if (!all)
            query = query.Where(x =>
                x.RiderId == subject
                || (
                    x.DriverId == subject
                    && x.ReservationId != null
                    && x.Status != TripStatus.Requested
                    && x.Status != TripStatus.AwaitingRiderConfirmation
                    && x.Status != TripStatus.ReservingDriver
                )
            );
        return await query.OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Trip>> Offers(string driverId, CancellationToken ct) =>
        await db
            .Trips.AsNoTracking()
            .Where(x => x.DriverId == driverId && x.Status == TripStatus.AwaitingDriverAcceptance)
            .ToListAsync(ct);

    public Task<Trip?> ByBookingKey(string riderId, string key, CancellationToken ct) =>
        db
            .Trips.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RiderId == riderId && x.IdempotencyKey == key, ct);

    public async Task<Trip> Create(Trip trip, CancellationToken ct)
    {
        db.Trips.Add(trip);
        try
        {
            await db.SaveChangesAsync(ct);
            return trip;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var existing = await ByBookingKey(trip.RiderId, trip.IdempotencyKey, ct);
            if (existing is null)
                throw;
            return existing;
        }
    }

    public async Task Save(CancellationToken ct)
    {
        var changes = db
            .ChangeTracker.Entries<Trip>()
            .Where(e => e.State == EntityState.Modified)
            .Select(e => e.Entity)
            .ToArray();
        await db.SaveChangesAsync(ct);
        foreach (var trip in changes)
            logger.LogInformation(
                "Trip {TripId} {Status} version {Version} attempt {Attempt} CorrelationId={CorrelationId}",
                trip.Id,
                trip.Status,
                trip.Version,
                trip.PaymentAttempt,
                trip.CorrelationId
            );
    }

    public async Task<IReadOnlyList<DueTrip>> Due(DateTimeOffset now, CancellationToken ct) =>
        await db
            .Trips.AsNoTracking()
            .Where(t =>
                (t.NextAttemptAt == null || t.NextAttemptAt <= now)
                && (
                    t.Status == TripStatus.Requested
                    || t.Status == TripStatus.ReservingDriver
                    || t.Status == TripStatus.AcceptingDriver
                    || (t.Status == TripStatus.AwaitingDriverAcceptance && t.Deadline <= now)
                    || (t.Status == TripStatus.AwaitingRiderConfirmation && t.Deadline <= now)
                )
            )
            .OrderBy(t => t.UpdatedAt)
            .Select(t => new DueTrip(t.Id, t.Version))
            .Take(20)
            .ToListAsync(ct);
}
