using SwiftRide.SharedKernel.Geometry;

namespace SwiftRide.Trips.Infrastructure;

public sealed class TripAccountSeeder(TripDb db, IConfiguration config)
{
    public async Task Seed(CancellationToken ct)
    {
        if (!config.GetValue<bool>("Fixtures:Enabled"))
            return;
        var riderPassword = config["Fixtures:RiderPassword"];
        var driverPassword = config["Fixtures:DriverPassword"];
        if (string.IsNullOrWhiteSpace(riderPassword) || string.IsNullOrWhiteSpace(driverPassword))
            throw new InvalidOperationException("Fixture rider and driver passwords are required.");

        var existingUsernames = await db.LocalAccounts.Select(x => x.Username).ToListAsync(ct);
        var usernames = existingUsernames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingProfileIds = await db.UserProfiles.Select(x => x.Id).ToListAsync(ct);
        var profileIds = existingProfileIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var riderHash = LocalAuthentication.HashPassword(riderPassword);
        var driverHash = LocalAuthentication.HashPassword(driverPassword);
        var accounts = new List<LocalAccount>(1100);
        var profiles = new List<UserProfile>(100);
        for (var i = 1; i <= 100; i++)
        {
            var random = new Random(20261114 + i);
            var username = $"rider{i:000}";
            var id = username;
            if (usernames.Add(username))
            {
                accounts.Add(
                    new LocalAccount
                    {
                        Id = id,
                        Username = username,
                        PasswordHash = riderHash,
                        Role = "rider",
                    }
                );
            }
            if (profileIds.Add(id))
            {
                var location = HcmcSeedLocation.Next(random);
                var profile = new UserProfile { Id = id };
                profile.Update($"Rider {i:000}", $"090{i:0000000}");
                profile.SetSeedLocation(location.Latitude, location.Longitude);
                profiles.Add(profile);
            }
        }
        for (var i = 1; i <= 1000; i++)
        {
            var username = $"driver{i:0000}";
            if (!usernames.Add(username))
                continue;
            accounts.Add(
                new LocalAccount
                {
                    Id = DriverId(i),
                    Username = username,
                    PasswordHash = driverHash,
                    Role = "driver",
                }
            );
        }
        db.LocalAccounts.AddRange(accounts);
        db.UserProfiles.AddRange(profiles);
        if (accounts.Count > 0 || profiles.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    public static string DriverId(int number) => $"22222222-2222-2222-2222-{number:000000000000}";
}
