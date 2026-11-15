using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SwiftRide.Infrastructure;
using Xunit;

public sealed class TransientFailureTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EfProviderWrapperPreservesDatabaseRetryClassification(bool transient)
    {
        var database = new DatabaseFailure(transient);
        var wrapped = new InvalidOperationException(
            "Provider failure",
            new DbUpdateException("Update failed", database)
        );
        Assert.Equal(transient, TransientErrors.IsTransient(wrapped));
        Assert.False(TransientErrors.IsTransient(new InvalidOperationException("Invalid command")));
    }

    private sealed class DatabaseFailure(bool transient) : DbException
    {
        public override bool IsTransient => transient;
    }
}
