namespace SwiftRide.Trips.Application.Ports;

public interface IUserProfileStore
{
    Task<UserProfile?> Get(string id, CancellationToken ct);
    Task Save(UserProfile profile, CancellationToken ct);
}
