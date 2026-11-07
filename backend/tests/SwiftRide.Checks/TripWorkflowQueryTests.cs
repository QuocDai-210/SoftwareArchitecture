using Moq;
using SwiftRide.Application.Security;
using SwiftRide.SharedKernel;
using SwiftRide.Trips.Application;
using SwiftRide.Trips.Application.Ports;
using SwiftRide.Trips.Domain;
using Xunit;

public sealed class TripWorkflowQueryTests
{
    [Fact]
    public async Task WorkflowReturnsDiagnosticFieldsForOps()
    {
        var id = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var trip = new Trip { Id = id, CorrelationId = "trace-id" };
        trip.ScheduleRetry(now, 0);
        var reader = new Mock<ITripReader>();
        reader.Setup(x => x.Get(id, It.IsAny<CancellationToken>())).ReturnsAsync(trip);

        var result = await new TripQueries(reader.Object).Workflow(
            id,
            new Actor("ops", IsOps: true),
            default
        );

        Assert.Equal(id, result.TripId);
        Assert.Equal(trip.Status, result.Status);
        Assert.Equal(1, result.RetryCount);
        Assert.Equal(now.AddSeconds(2), result.NextAttemptAt);
        Assert.Equal(trip.Reason, result.LastReason);
        Assert.Equal("trace-id", result.CorrelationId);
        Assert.Equal(now, result.UpdatedAt);
    }

    [Fact]
    public async Task WorkflowRejectsNonOpsBeforeReadingTrip()
    {
        var reader = new Mock<ITripReader>();

        var exception = await Assert.ThrowsAsync<RuleException>(() =>
            new TripQueries(reader.Object).Workflow(Guid.NewGuid(), new Actor("rider"), default)
        );

        Assert.Equal(ErrorKind.Forbidden, exception.Kind);
        reader.Verify(x => x.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
