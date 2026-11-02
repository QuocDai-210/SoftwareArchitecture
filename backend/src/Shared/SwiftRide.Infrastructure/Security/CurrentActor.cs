using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SwiftRide.Application.Security;
using SwiftRide.SharedKernel;

namespace SwiftRide.Infrastructure;

public static class CurrentActor
{
    public static string Subject(this ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value
        ?? throw new RuleException("Thiếu danh tính.", ErrorKind.Unauthenticated);

    public static string Correlation(this HttpContext context) =>
        (string)context.Items["CorrelationId"]!;

    public static Actor Actor(this ClaimsPrincipal user) =>
        new(
            user.Subject(),
            user.IsInRole("user"),
            user.IsInRole("driver"),
            user.IsInRole("ops"),
            user.IsInRole("service")
        );
}
