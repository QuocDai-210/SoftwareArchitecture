using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
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
        builder.Services.AddSingleton<IMatchingMetrics>(s =>
            s.GetRequiredService<ApplicationMetrics>()
        );
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        var authority =
            builder.Configuration["Auth:Authority"]
            ?? throw new InvalidOperationException("Auth:Authority is required.");
        builder
            .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = authority;
                o.Audience = "swiftride-api";
                o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                o.MapInboundClaims = false;
                o.BackchannelTimeout = TimeSpan.FromSeconds(5);
                o.TokenValidationParameters.ValidIssuer = authority;
                o.TokenValidationParameters.RoleClaimType = "role";
                o.TokenValidationParameters.NameClaimType = "preferred_username";
                o.Events.OnTokenValidated = context =>
                {
                    if (string.IsNullOrWhiteSpace(context.Principal?.FindFirst("sub")?.Value))
                        context.Fail("Missing subject.");
                    return Task.CompletedTask;
                };
            });
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy("participant", p => p.RequireRole("rider", "driver"))
            .AddPolicy("payment-read", p => p.RequireRole("rider", "service"))
            .AddPolicy("rider", p => p.RequireRole("rider"))
            .AddPolicy("driver", p => p.RequireRole("driver"))
            .AddPolicy("service", p => p.RequireRole("service"));
        builder.Services.AddHttpClient("identity", c => c.Timeout = TimeSpan.FromSeconds(5));
        builder.Services.AddSingleton<ServiceTokens>();
        builder.Services.AddTransient<ServiceAuthHandler>();
        builder
            .Services.AddHttpClient(
                "matching",
                c =>
                {
                    c.BaseAddress = new Uri(
                        builder.Configuration["Services:Matching"] ?? "http://matching:8080"
                    );
                    c.Timeout = TimeSpan.FromSeconds(5);
                }
            )
            .AddHttpMessageHandler<ServiceAuthHandler>();
        if (builder.Configuration.GetValue<bool>("OTEL_SDK_DISABLED"))
            return;
        builder
            .Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(service))
            .WithTracing(t =>
                t.AddSource("SwiftRide", "MassTransit")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
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
