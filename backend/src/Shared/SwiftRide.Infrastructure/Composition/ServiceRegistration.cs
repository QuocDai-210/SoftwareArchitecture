using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
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
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        var signingKey = builder.Configuration["Auth:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
            throw new InvalidOperationException("Auth:SigningKey must contain at least 32 bytes.");
        var issuer = builder.Configuration["Auth:Issuer"] ?? "swiftride-local";
        const string local = JwtBearerDefaults.AuthenticationScheme;
        var authority = builder.Configuration["Auth:Authority"];
        var federated = !string.IsNullOrWhiteSpace(authority);
        var authentication = builder
            .Services.AddAuthentication(local)
            .AddJwtBearer(o =>
            {
                if (federated)
                    o.Events.OnTokenValidated = context =>
                    {
                        if (context.Principal?.IsInRole("service") != true)
                            context.Fail("User tokens must come from the configured authority.");
                        return Task.CompletedTask;
                    };
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = "swiftride-api",
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "preferred_username",
                    RoleClaimType = "role",
                };
            });
        var authorization = builder.Services.AddAuthorizationBuilder();
        var users = local;
        if (federated)
        {
            users = "oidc";
            authentication.AddJwtBearer(
                users,
                o =>
                {
                    o.Authority = authority;
                    o.Audience = "swiftride-api";
                    o.MapInboundClaims = false;
                    o.BackchannelTimeout = TimeSpan.FromSeconds(10);
                    o.TokenValidationParameters.RoleClaimType = "role";
                    o.TokenValidationParameters.NameClaimType = "preferred_username";
                    o.Events.OnTokenValidated = context =>
                    {
                        var user = context.Principal;
                        if (
                            string.IsNullOrWhiteSpace(user?.FindFirst("sub")?.Value)
                            || user.IsInRole("service")
                            || !(user.IsInRole("rider") || user.IsInRole("driver"))
                        )
                            context.Fail("Expected a Rider or Driver access token.");
                        return Task.CompletedTask;
                    };
                }
            );
            authorization.SetDefaultPolicy(
                new AuthorizationPolicyBuilder(local, users).RequireAuthenticatedUser().Build()
            );
        }
        authorization
            .AddPolicy("rider", p => p.AddAuthenticationSchemes(users).RequireRole("rider"))
            .AddPolicy("driver", p => p.AddAuthenticationSchemes(users).RequireRole("driver"))
            .AddPolicy("service", p => p.AddAuthenticationSchemes(local).RequireRole("service"));
        builder.Services.AddSingleton<ServiceTokens>();
        builder.Services.AddTransient<ServiceAuthHandler>();
        builder.Services.AddTransient<InternalRetryHandler>();
        builder
            .Services.AddHttpClient("matching", c =>
            {
                c.BaseAddress = new Uri(
                    builder.Configuration["Services:Matching"] ?? "http://matching:8080"
                );
                c.Timeout = TimeSpan.FromSeconds(20);
            })
            .AddHttpMessageHandler<ServiceAuthHandler>()
            .AddHttpMessageHandler<InternalRetryHandler>();
        if (builder.Configuration.GetValue<bool>("OTEL_SDK_DISABLED"))
            return;
        builder
            .Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(service))
            .WithTracing(t =>
                t.AddSource("SwiftRide", "MassTransit")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation(o =>
                        o.FilterHttpRequestMessage = request =>
                            request.RequestUri?.Host is not "api.geoapify.com"
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
