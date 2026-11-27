using System.Diagnostics;
using System.Text.Json.Serialization;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftRide.Application.Messaging;

namespace SwiftRide.Infrastructure;

public static class MessageBusRegistration
{
    public static void AddReliableBus<TDb>(
        this WebApplicationBuilder builder,
        Action<IBusRegistrationConfigurator> consumers
    )
        where TDb : DbContext
    {
        builder.Services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
        if (!builder.Configuration.GetValue<bool>("OTEL_SDK_DISABLED"))
            builder.Services.AddHostedService<OutboxMetrics<TDb>>();
        builder.Services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            consumers(x);
            x.AddEntityFrameworkOutbox<TDb>(o =>
            {
                o.QueryDelay = TimeSpan.FromSeconds(1);
                o.UsePostgres();
                o.UseBusOutbox();
            });
            x.AddConfigureEndpointsCallback(
                (context, _, endpoint) =>
                {
                    endpoint.UseMessageRetry(r =>
                    {
                        r.Handle<Exception>(TransientErrors.IsTransient);
                        r.Exponential(
                            3,
                            TimeSpan.FromSeconds(1),
                            TimeSpan.FromSeconds(15),
                            TimeSpan.FromSeconds(2)
                        );
                    });
                    endpoint.UseEntityFrameworkOutbox<TDb>(context);
                }
            );
            x.UsingRabbitMq(
                (context, cfg) =>
                {
                    cfg.Host(
                        builder.Configuration["Rabbit:Host"] ?? "rabbitmq",
                        "/",
                        host =>
                        {
                            host.Username(builder.Configuration["Rabbit:Username"]!);
                            host.Password(builder.Configuration["Rabbit:Password"]!);
                        }
                    );
                    cfg.ConfigureJsonSerializerOptions(options =>
                    {
                        options.Converters.Add(new JsonStringEnumConverter());
                        return options;
                    });
                    cfg.ConfigureEndpoints(context);
                }
            );
        });
    }
}
