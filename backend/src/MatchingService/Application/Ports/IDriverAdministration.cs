namespace SwiftRide.Matching.Application.Ports;

public interface IDriverAdministration
{
    Task Create(DriverApplication application, CancellationToken ct);
    Task<DriverApplication> Application(Guid id, CancellationToken ct);
    Task<PageResult<DriverApplication>> Applications(int page, CancellationToken ct);
    Task Save(DriverApplication application, CancellationToken ct);
    Task Register(DriverApplication application, CancellationToken ct);
    Task<bool> Approved(string subject, CancellationToken ct);
    Task Suspend(string subject, bool suspended, CancellationToken ct);
}
