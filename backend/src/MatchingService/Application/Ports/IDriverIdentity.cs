namespace SwiftRide.Matching.Application.Ports;

public interface IDriverIdentity
{
    Task<string> Provision(
        DriverApplication application,
        string temporaryPassword,
        CancellationToken ct
    );
    Task Enable(string subject, bool enabled, CancellationToken ct);
}
