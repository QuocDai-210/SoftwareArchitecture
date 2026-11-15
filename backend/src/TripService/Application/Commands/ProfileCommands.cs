using SwiftRide.Trips.Application.Ports;

namespace SwiftRide.Trips.Application;

public sealed class ProfileCommands(IUserProfileStore store)
{
    public async Task<UserProfile> Get(Actor actor, CancellationToken ct) =>
        await store.Get(actor.Id, ct) ?? new UserProfile { Id = actor.Id };

    public async Task<UserProfile> Update(ProfileRequest request, Actor actor, CancellationToken ct)
    {
        if (!actor.IsRider && !actor.IsDriver)
            throw new RuleException("Cần tài khoản người dùng.", ErrorKind.Forbidden);
        var profile = await store.Get(actor.Id, ct) ?? new UserProfile { Id = actor.Id };
        profile.Update(request.Name, request.Phone);
        await store.Save(profile, ct);
        return profile;
    }
}
