using System.Diagnostics;
using System.Data.Common;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SwiftRide.Core;

namespace SwiftRide.Hosting;

public static class Telemetry
{
    public static readonly ActivitySource Activities = new("SwiftRide");
    public static readonly Meter Meter = new("SwiftRide");
    public static readonly Counter<long> Trips = Meter.CreateCounter<long>("swiftride.trips.requested");
    public static readonly Counter<long> Outcomes = Meter.CreateCounter<long>("swiftride.trips.outcomes");
    public static readonly Counter<long> Payments = Meter.CreateCounter<long>("swiftride.payments.outcomes");
    public static readonly Histogram<double> Matching = Meter.CreateHistogram<double>("swiftride.matching.duration", "s");
}
public static class Platform
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    public static async Task<bool> HealthProbe(string[] args)
    {
        if (!args.Contains("--health")) return false;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try { using var response = await client.GetAsync("http://127.0.0.1:8080/health/ready"); Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1; }
        catch (Exception) { Environment.ExitCode = 1; }
        return true;
    }
    public static void AddPlatform(this WebApplicationBuilder builder, string service)
    {
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.Authority = builder.Configuration["Auth:Authority"] ?? throw new InvalidOperationException("Auth:Authority required");
            o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            o.MapInboundClaims = false;
            o.TokenValidationParameters.ValidIssuer = builder.Configuration["Auth:Issuer"];
            o.TokenValidationParameters.ValidAudience = "swiftride-api";
            o.TokenValidationParameters.NameClaimType = "preferred_username";
            o.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    var identity = (ClaimsIdentity)context.Principal!.Identity!;
                    var realm = context.Principal.FindFirst("realm_access")?.Value;
                    if (realm is not null)
                    {
                        using var doc = JsonDocument.Parse(realm);
                        if (doc.RootElement.TryGetProperty("roles", out var roles))
                            foreach (var role in roles.EnumerateArray())
                                identity.AddClaim(new Claim(ClaimTypes.Role, role.GetString()!));
                    }
                    return Task.CompletedTask;
                }
            };
        });
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("rider", p => p.RequireRole("rider"))
            .AddPolicy("driver", p => p.RequireRole("driver"))
            .AddPolicy("service", p => p.RequireRole("service"))
            .AddPolicy("ops", p => p.RequireRole("ops"));
        builder.Services.AddSingleton<ServiceTokens>();
        builder.Services.AddTransient<ServiceAuthHandler>();
        builder.Services.AddTransient<InternalRetryHandler>();
        builder.Services.AddHttpClient("identity", c => c.Timeout = TimeSpan.FromSeconds(5));
        builder.Services.AddHttpClient("matching", c =>
        {
            c.BaseAddress = new Uri(builder.Configuration["Services:Matching"] ?? "http://matching:8080");
            c.Timeout = TimeSpan.FromSeconds(20);
        }).AddHttpMessageHandler<ServiceAuthHandler>().AddHttpMessageHandler<InternalRetryHandler>();
        builder.Services.AddHttpClient("payment", c =>
        {
            c.BaseAddress = new Uri(builder.Configuration["Services:Payment"] ?? "http://payment:8080");
            c.Timeout = TimeSpan.FromSeconds(20);
        }).AddHttpMessageHandler<ServiceAuthHandler>().AddHttpMessageHandler<InternalRetryHandler>();
        builder.Services.AddOpenTelemetry().ConfigureResource(r => r.AddService(service))
            .WithTracing(t => t.AddSource("SwiftRide", "MassTransit").AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporter())
            .WithMetrics(m => m.AddMeter("SwiftRide").AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporter());
        builder.Logging.AddOpenTelemetry(o => { o.IncludeFormattedMessage = true; o.IncludeScopes = true; o.AddOtlpExporter(); });
    }
    public static void UsePlatform(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var correlation = context.Request.Headers["CorrelationId"].FirstOrDefault();
            if (!Guid.TryParse(correlation, out _)) correlation = Guid.NewGuid().ToString();
            context.Items["CorrelationId"] = correlation;
            context.Request.Headers["CorrelationId"] = correlation;
            context.Response.Headers["CorrelationId"] = correlation;
            using var scope = app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlation });
            try { await next(context); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (Exception ex)
            {
                var (status, detail) = ex switch
                {
                    BadHttpRequestException bad => (bad.StatusCode, "Dữ liệu HTTP không hợp lệ."),
                    JsonException => (400, "JSON không hợp lệ."),
                    RuleException rule => (rule.Status, rule.Message),
                    DbUpdateConcurrencyException => (409, "Dữ liệu vừa thay đổi. Hãy tải lại."),
                    DbUpdateException => (409, "Dữ liệu xung đột. Hãy tải lại trạng thái trước khi thử lại."),
                    HttpRequestException => (503, "Dịch vụ tạm thời chưa sẵn sàng."),
                    OperationCanceledException => (503, "Dịch vụ phản hồi quá hạn."),
                    _ => (500, "Lỗi hệ thống. Dùng CorrelationId để tra cứu.")
                };
                if (status == 500) app.Logger.LogError(ex, "Request failed {CorrelationId}", correlation);
                if (!context.Response.HasStarted)
                    await Results.Problem(statusCode: status, detail: detail,
                        extensions: new Dictionary<string, object?> { ["correlationId"] = correlation }).ExecuteAsync(context);
            }
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
        app.MapOpenApi().RequireAuthorization("ops");
    }
    public static bool IsTransient(Exception error) => error switch
    {
        TimeoutException or OperationCanceledException or DbUpdateConcurrencyException => true,
        HttpRequestException h => h.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int?)h.StatusCode >= 500,
        DbUpdateException { InnerException: DbException db } => db.IsTransient,
        DbException db => db.IsTransient,
        _ => false
    };
    public static string Subject(this ClaimsPrincipal user) => user.FindFirst("sub")?.Value
        ?? throw new RuleException("Thiếu danh tính.", 401);
    public static string Correlation(this HttpContext context) => (string)context.Items["CorrelationId"]!;
    public static async Task<T> Read<T>(this HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
                throw new RuleException("Tài nguyên đã đổi trạng thái hoặc không tồn tại.", (int)response.StatusCode);
            if (response.StatusCode == HttpStatusCode.BadRequest) throw new RuleException("Dữ liệu không hợp lệ.", 400);
            response.EnsureSuccessStatusCode();
        }
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }
    public static void JsonColumn<T>(this PropertyBuilder<T> property)
    {
        property.HasConversion(v => JsonSerializer.Serialize(v, Json), v => JsonSerializer.Deserialize<T>(v, Json)!);
        property.Metadata.SetValueComparer(new ValueComparer<T>(
            (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
            v => JsonSerializer.Serialize(v, Json).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Json), Json)!));
    }
    public static void AddReliableBus<TDb>(this WebApplicationBuilder builder, bool postgres,
        Action<IBusRegistrationConfigurator> consumers) where TDb : DbContext
    {
        builder.Services.AddHostedService<OutboxMetrics<TDb>>();
        builder.Services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            consumers(x);
            x.AddEntityFrameworkOutbox<TDb>(o =>
            {
                o.QueryDelay = TimeSpan.FromSeconds(1);
                if (postgres) o.UsePostgres(); else o.UseSqlServer();
                o.UseBusOutbox();
            });
            x.AddConfigureEndpointsCallback((context, _, endpoint) =>
            {
                endpoint.UseMessageRetry(r =>
                {
                    r.Handle<Exception>(IsTransient);
                    r.Exponential(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(2));
                });
                endpoint.UseEntityFrameworkOutbox<TDb>(context);
            });
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(builder.Configuration["Rabbit:Host"] ?? "rabbitmq", "/", host =>
                {
                    host.Username(builder.Configuration["Rabbit:Username"]!);
                    host.Password(builder.Configuration["Rabbit:Password"]!);
                });
                cfg.ConfigureJsonSerializerOptions(options => { options.Converters.Add(new JsonStringEnumConverter()); return options; });
                cfg.ConfigureEndpoints(context);
            });
        });
    }
    public static void Outbox(this ModelBuilder model)
    {
        model.AddInboxStateEntity(); model.AddOutboxMessageEntity(); model.AddOutboxStateEntity();
    }
    public static Task Emit<T>(this IPublishEndpoint bus, T message, string correlation, CancellationToken ct = default) where T : class
        => bus.Publish(message, context =>
        {
            if (Guid.TryParse(correlation, out var id)) context.CorrelationId = id;
            context.Headers.Set("CorrelationId", correlation);
            context.Headers.Set("SchemaVersion", 1);
        }, ct);
}
public sealed class ServiceTokens(IHttpClientFactory clients, IConfiguration config)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expires;
    public async Task<string> Get(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_token is not null && _expires > DateTimeOffset.UtcNow.AddSeconds(30)) return _token;
            using var response = await clients.CreateClient("identity").PostAsync(
                $"{config["Auth:Authority"]}/protocol/openid-connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials", ["client_id"] = "swiftride-service",
                    ["client_secret"] = config["Auth:ServiceSecret"]!
                }), ct);
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            _token = data.GetProperty("access_token").GetString()!;
            _expires = DateTimeOffset.UtcNow.AddSeconds(data.GetProperty("expires_in").GetInt32());
            return _token;
        }
        finally { _gate.Release(); }
    }
}
public sealed class ServiceAuthHandler(ServiceTokens tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.Get(ct));
        return await base.SendAsync(request, ct);
    }
}
public sealed class InternalRetryHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct);
        for (var attempt = 0; ; attempt++)
        {
            using var copy = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var h in request.Headers) copy.Headers.TryAddWithoutValidation(h.Key, h.Value);
            if (body is not null)
            {
                copy.Content = new ByteArrayContent(body);
                foreach (var h in request.Content!.Headers) copy.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var response = await base.SendAsync(copy, timeout.Token);
                if (attempt == 3 || ((int)response.StatusCode < 500 && response.StatusCode != HttpStatusCode.RequestTimeout && response.StatusCode != HttpStatusCode.TooManyRequests))
                    return response;
                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < 3) { }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < 3) { }
            await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt) + Random.Shared.Next(100)), ct);
        }
    }
}

public sealed class OutboxMetrics<TDb>(IServiceScopeFactory scopes, ILogger<OutboxMetrics<TDb>> logger) : BackgroundService where TDb : DbContext
{
    private long _count;
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _ = Telemetry.Meter.CreateObservableGauge("swiftride.outbox.messages", () => Interlocked.Read(ref _count));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TDb>();
                var sql = db.Database.ProviderName?.Contains("SqlServer", StringComparison.Ordinal) == true
                    ? "SELECT COUNT_BIG(*) AS [Value] FROM [OutboxMessage]" : "SELECT COUNT(*) AS \"Value\" FROM \"OutboxMessage\"";
                Interlocked.Exchange(ref _count, await db.Database.SqlQueryRaw<long>(sql).SingleAsync(ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning(ex, "Could not sample outbox depth"); }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}
