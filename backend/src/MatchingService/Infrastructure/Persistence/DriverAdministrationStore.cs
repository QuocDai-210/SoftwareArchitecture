namespace SwiftRide.Matching.Infrastructure;

public sealed class DriverAdministrationStore(IMongoDatabase db) : IDriverAdministration
{
    private readonly IMongoCollection<DriverApplication> applications = db.GetCollection<DriverApplication>("driverApplications");
    private readonly IMongoCollection<DriverDocument> drivers = db.GetCollection<DriverDocument>("drivers");

    public async Task Create(DriverApplication application, CancellationToken ct)
    {
        await applications.Indexes.CreateOneAsync(new CreateIndexModel<DriverApplication>(Builders<DriverApplication>.IndexKeys.Ascending(x => x.Username), new CreateIndexOptions { Unique = true }), cancellationToken: ct);
        try { await applications.InsertOneAsync(application, cancellationToken: ct); }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        { throw new RuleException("Tên đăng nhập đã có hồ sơ."); }
    }

    public async Task<DriverApplication> Application(Guid id, CancellationToken ct) =>
        await applications.Find(x => x.Id == id).FirstOrDefaultAsync(ct)
        ?? throw new RuleException("Không có hồ sơ tài xế.", ErrorKind.NotFound);

    public async Task<IReadOnlyList<DriverApplication>> Applications(CancellationToken ct) =>
        await applications.Find(Builders<DriverApplication>.Filter.Empty).SortByDescending(x => x.CreatedAt).Limit(200).ToListAsync(ct);

    public async Task Save(DriverApplication application, CancellationToken ct)
    {
        var allowed = application.Status switch
        {
            "Provisioning" => new[] { "Pending", "Provisioning" },
            "Approved" => new[] { "Provisioning", "Approved" },
            _ => new[] { "Pending" }
        };
        var result = await applications.ReplaceOneAsync(Builders<DriverApplication>.Filter.Eq(x => x.Id, application.Id)
            & Builders<DriverApplication>.Filter.In(x => x.Status, allowed), application, cancellationToken: ct);
        if (result.MatchedCount != 1) throw new RuleException("Hồ sơ vừa thay đổi. Hãy tải lại.");
    }

    public Task Register(DriverApplication application, CancellationToken ct) => drivers.UpdateOneAsync(x => x.Id == application.Subject,
        Builders<DriverDocument>.Update.Set(x => x.Name, application.Name).Set(x => x.Approved, true)
            .Set(x => x.Plate, application.Plate).Set(x => x.Vehicle, application.Vehicle)
            .SetOnInsert(x => x.Location, DriverDocument.Geo(106.7009, 10.7769))
            .SetOnInsert(x => x.LastSeenAt, DateTime.UtcNow).SetOnInsert(x => x.Available, false).SetOnInsert(x => x.Busy, false),
        new UpdateOptions { IsUpsert = true }, ct);

    public Task<bool> Approved(string subject, CancellationToken ct) => drivers.Find(x => x.Id == subject && x.Approved && !x.Suspended).AnyAsync(ct);

    public async Task Suspend(string subject, bool suspended, CancellationToken ct)
    {
        var result = await drivers.UpdateOneAsync(x => x.Id == subject && !x.Busy,
            Builders<DriverDocument>.Update.Set(x => x.Suspended, suspended).Set(x => x.Available, false), cancellationToken: ct);
        if (result.MatchedCount == 0) throw new RuleException("Không có tài xế hoặc tài xế đang phục vụ chuyến.");
    }
}
