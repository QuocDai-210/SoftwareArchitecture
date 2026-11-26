using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SwiftRide.Application.Health;
using SwiftRide.SharedKernel;

namespace SwiftRide.Infrastructure;

public static class ApiPipeline
{
    public static void UsePlatform(this WebApplication app)
    {
        app.Use(
            async (context, next) =>
            {
                var correlation = context.Request.Headers["CorrelationId"].FirstOrDefault();
                if (!Guid.TryParse(correlation, out _))
                    correlation = Guid.NewGuid().ToString();
                context.Items["CorrelationId"] = correlation;
                context.Request.Headers["CorrelationId"] = correlation;
                context.Response.Headers["CorrelationId"] = correlation;
                using var scope = app.Logger.BeginScope(
                    new Dictionary<string, object> { ["CorrelationId"] = correlation }
                );
                var started = Stopwatch.GetTimestamp();
                try
                {
                    await next(context);
                }
                catch (OperationCanceledException)
                    when (context.RequestAborted.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    var (status, detail) = ex switch
                    {
                        BadHttpRequestException bad => (
                            bad.StatusCode,
                            "Dữ liệu HTTP không hợp lệ."
                        ),
                        JsonException => (400, "JSON không hợp lệ."),
                        RuleException rule => (
                            rule.Kind switch
                            {
                                ErrorKind.Validation => 400,
                                ErrorKind.Unauthenticated => 401,
                                ErrorKind.Forbidden => 403,
                                ErrorKind.NotFound => 404,
                                ErrorKind.Unavailable => 503,
                                _ => 409,
                            },
                            rule.Message
                        ),
                        DbUpdateConcurrencyException => (409, "Dữ liệu vừa thay đổi. Hãy tải lại."),
                        DbUpdateException => (
                            409,
                            "Dữ liệu xung đột. Hãy tải lại trạng thái trước khi thử lại."
                        ),
                        HttpRequestException => (503, "Dịch vụ tạm thời chưa sẵn sàng."),
                        OperationCanceledException => (503, "Dịch vụ phản hồi quá hạn."),
                        _ => (500, "Lỗi hệ thống. Dùng CorrelationId để tra cứu."),
                    };
                    if (status == 500)
                        app.Logger.LogError(ex, "Request failed {CorrelationId}", correlation);
                    if (!context.Response.HasStarted)
                        await Results
                            .Problem(
                                statusCode: status,
                                detail: detail,
                                extensions: new Dictionary<string, object?>
                                {
                                    ["correlationId"] = correlation,
                                }
                            )
                            .ExecuteAsync(context);
                }
                if (
                    !context.Request.Path.StartsWithSegments("/health")
                    && !context.RequestAborted.IsCancellationRequested
                )
                {
                    var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
                    app.Logger.LogInformation(
                        "{Method} {Route} {StatusCode} {ElapsedMs}ms CorrelationId={CorrelationId}",
                        context.Request.Method,
                        route ?? "(unmatched)",
                        context.Response.StatusCode,
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                        correlation
                    );
                }
            }
        );
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
        app.MapGet("/health/ready", async (IReadinessProbe probe, CancellationToken ct) =>
                await probe.Ready(ct)
                    ? Results.Ok(new { status = "ready" })
                    : Results.StatusCode(503)
            )
            .AllowAnonymous();
        app.MapOpenApi().RequireAuthorization();
    }
}
