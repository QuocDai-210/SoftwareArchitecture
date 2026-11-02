namespace SwiftRide.Matching.Application.Ports;

public interface IMapProvider
{
    Task<IReadOnlyList<SwiftRide.SharedKernel.Point>> Search(string text, CancellationToken ct);
    Task<SwiftRide.SharedKernel.Point> Reverse(double latitude, double longitude, CancellationToken ct);
    Task<SwiftRide.SharedKernel.RoadRoute> Route(SwiftRide.SharedKernel.Point from, SwiftRide.SharedKernel.Point to, CancellationToken ct);
}
