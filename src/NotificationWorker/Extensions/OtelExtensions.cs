using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using NotificationWorker.Metrics;

namespace NotificationWorker.Extensions;

public static class OtelExtensions
{
    public const string ActivitySourceName = "NotificationWorker.Worker";

    public static IServiceCollection AddOtelInstrumentation(this IServiceCollection services, IConfiguration configuration)
    {
        var serviceName = configuration["OTEL_SERVICE_NAME"] ?? "notification-worker";
        var serviceVersion = typeof(OtelExtensions).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
        var topic = configuration["KAFKA_TOPIC_NOTIFICATIONS"] ?? "notifications";
        var consumerGroup = configuration["KAFKA_GROUP_ID_NOTIFICATION"] ?? "notification-worker";
        var resourceBuilder = ResourceBuilder.CreateDefault().AddService(serviceName, serviceVersion: serviceVersion);

        services.AddSingleton(new KafkaLagSnapshot(topic, consumerGroup));
        services.AddSingleton<INotificationMetrics, NotificationMetrics>();
        services.AddSingleton<NotificationLagRefresher>();

        services
            .AddOpenTelemetry()
            .WithTracing(builder => builder
                .SetResourceBuilder(resourceBuilder)
                .AddSource(ActivitySourceName)
                .AddEntityFrameworkCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                    options.Protocol = OtlpExportProtocol.Grpc;
                }))
            .WithMetrics(builder => builder
                .SetResourceBuilder(resourceBuilder)
                .AddMeter(NotificationMetrics.MeterName)
                .SetExemplarFilter(ExemplarFilterType.AlwaysOn)
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                    options.Protocol = OtlpExportProtocol.Grpc;
                }));
        services.AddLogging(logging =>
            logging.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(resourceBuilder);
                options.AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = new Uri(otlpEndpoint);
                    exporter.Protocol = OtlpExportProtocol.Grpc;
                });
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
            }));
        return services;
    }
}