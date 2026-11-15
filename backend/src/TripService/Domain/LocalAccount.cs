namespace SwiftRide.Trips.Domain;

public sealed class LocalAccount
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
}
