namespace SwiftRide.Trips.Infrastructure;

public sealed class UserProfileStore(TripDb db) : IUserProfileStore
{
    public Task<UserProfile?> Get(string id, CancellationToken ct) => db.UserProfiles.SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task Save(UserProfile profile, CancellationToken ct)
    {
        if (db.Entry(profile).State == EntityState.Detached) db.UserProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
    }
}
