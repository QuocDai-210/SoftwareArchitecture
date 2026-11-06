using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SwiftRide.Application.Observability;
using SwiftRide.SharedKernel;

namespace SwiftRide.Infrastructure;

public static class ServiceRegistration
{
    public static void AddPlatform(this WebApplicationBuilder builder, string service)
    {
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            var resolver =
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(info =>
            {
                if (info.Type == typeof(Point))
                    foreach (var property in info.Properties)
                        property.IsRequired = true;
            });
            o.SerializerOptions.TypeInfoResolver = resolver;
        });
        builder.Services.AddSingleton<ApplicationMetrics>();
        builder.Services.AddSingleton<ITripMetrics>(s =>
            s.GetRequiredService<ApplicationMetrics>()
        );
        builder.Services.AddSingleton<IPaymentMetrics>(s =>
            s.GetRequiredService<ApplicationMetrics>()
        );
        builder.Services.AddSingleton<IMatchingMetrics>(s =>
            s.GetRequiredService<ApplicationMetrics>()
        );
        builder.Services.AddSingleton<
            SwiftRide.Application.Payments.IPaymentTokenPolicy,
            DemoPaymentTokenPolicy
        >();
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        builder
            .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority =
                    builder.Configuration["Auth:Authority"]
                    ?? throw new InvalidOperationException("Auth:Authority required");
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
                                    identity.AddClaim(
                                        new Claim(ClaimTypes.Role, role.GetString()!)
                                    );
                        }
                        return Task.CompletedTask;
                    },
                };
            });
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy("rider", p => p.RequireRole("user"))
            .AddPolicy("user", p => p.RequireRole("user"))
            .AddPolicy("driver", p => p.RequireRole("driver"))
            .AddPolicy("service", p => p.RequireRole("service"))
            .AddPolicy("ops", p => p.RequireRole("ops"));
        builder.Services.AddSingleton<ServiceTokens>();
        builder.Services.AddTransient<ServiceAuthHandler>();
        builder.Services.AddTransient<InternalRetryHandler>();
        builder.Services.AddHttpClient("identity", c => c.Timeout = TimeSpan.FromSeconds(5));
        builder
            .Services.AddHttpClient(
                "matching",
                c =>
                {
                    c.BaseAddress = new Uri(
                        builder.Configuration["Services:Matching"] ?? "http://matching:8080"
                    );
                    c.Timeout = TimeSpan.FromSeconds(20);
                }
            )
            .AddHttpMessageHandler<ServiceAuthHandler>()
            .AddHttpMessageHandler<InternalRetryHandler>();
        builder
            .Services.AddHttpClient(
                "payment",
                c =>
                {
                    c.BaseAddress = new Uri(
                        builder.Configuration["Services:Payment"] ?? "http://payment:8080"
                    );
                    c.Timeout = TimeSpan.FromSeconds(20);
                }
            )
            .AddHttpMessageHandler<ServiceAuthHandler>()
            .AddHttpMessageHandler<InternalRetryHandler>();
        if (builder.Configuration.GetValue<bool>("OTEL_SDK_DISABLED"))
            return;
        builder
            .Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(service))
            .WithTracing(t =>
                t.AddSource("SwiftRide", "MassTransit")
                    .AddAspNetCoreInstrumentation(o =>
                        o.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/payments/vnpay")
                    )
                    .AddHttpClientInstrumentation(o =>
                        o.FilterHttpRequestMessage = request =>
                            request.RequestUri?.Host
                                is not "api.geoapify.com"
                                    and not "sandbox.vnpayment.vn"
                            && request.RequestUri?.AbsolutePath != "/payments/vnpay/ipn"
                    )
                    .AddOtlpExporter()
            )
            .WithMetrics(m =>
                m.AddMeter("SwiftRide")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter()
            );
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
            o.AddOtlpExporter();
        });
    }
}
