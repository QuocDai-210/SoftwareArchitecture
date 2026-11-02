namespace SwiftRide.Trips.Infrastructure;

public sealed class TripRepository(TripDb db) : ITripReader, ITripWriter, ITripWorkflowQueue
{
    public async Task<PageResult<TripView>> History(string subject, bool all, int page, string? status, CancellationToken ct)
    {
        var query = db.Trips.AsNoTracking().AsQueryable();
        if (!all) query = query.Where(x => x.RiderId == subject || (x.DriverId == subject
            && x.Status != TripStatus.Requested && x.Status != TripStatus.AwaitingRiderConfirmation && x.Status != TripStatus.ReservingDriver));
        if (status is not null)
        {
            if (!Enum.TryParse<TripStatus>(status, out var parsed) || !Enum.IsDefined(parsed)) throw new RuleException("Trạng thái không hợp lệ.", ErrorKind.Validation);
            query = query.Where(x => x.Status == parsed);
        }
        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((page - 1) * 10).Take(10).ToListAsync(ct);
        return new(items.Select(TripView.From).ToArray(), page, 10, total);
    }

    public async Task<object> Statistics(CancellationToken ct)
    {
        var statuses = await db.Trips.AsNoTracking().GroupBy(x => x.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(ct);
        return new { total = statuses.Sum(x => x.Count), statuses };
    }
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

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<IReadOnlyList<Guid>> Due(DateTimeOffset now, CancellationToken ct) =>
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
            .Select(t => t.Id)
            .Take(20)
            .ToListAsync(ct);
}
