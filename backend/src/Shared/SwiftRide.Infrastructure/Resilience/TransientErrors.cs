using System.Data.Common;
using System.Net;
using Microsoft.EntityFrameworkCore;
using SwiftRide.SharedKernel;

namespace SwiftRide.Infrastructure;

public static class TransientErrors
{
    public static bool IsTransient(Exception error) =>
        error switch
        {
            TimeoutException or OperationCanceledException or DbUpdateConcurrencyException => true,
            RuleException { Kind: ErrorKind.Unavailable } => true,
            HttpRequestException h => h.StatusCode
                is null
                    or HttpStatusCode.RequestTimeout
                    or HttpStatusCode.TooManyRequests
                || (int?)h.StatusCode >= 500,
            DbUpdateException { InnerException: DbException db } => db.IsTransient,
            DbException db => db.IsTransient,
            _ => false,
        };
}
