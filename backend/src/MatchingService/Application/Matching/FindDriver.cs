using System.Diagnostics;
using SwiftRide.Matching.Application.Ports;

namespace SwiftRide.Matching.Application;

public sealed class FindDriver(IDriverReader reader, IMatchingMetrics metrics)
{
    public async Task<MatchResponse> Execute(MatchRequest request, CancellationToken ct)
    {
        if (request.Pickup is null || request.ExcludedDrivers is null)
            throw new RuleException("Thiếu dữ liệu matching.", ErrorKind.Validation);
        request.Pickup.Validate();
        if (request.ExcludedDrivers.Length > 100)
            throw new RuleException("Danh sách loại trừ quá dài.", ErrorKind.Validation);
        var watch = Stopwatch.StartNew();
        try
        {
            return new(await reader.Nearest(request.Pickup, request.ExcludedDrivers, ct));
        }
        finally
        {
            metrics.Duration(watch.Elapsed.TotalSeconds);
        }
    }
}
