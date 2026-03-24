using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderService.Metrics;

namespace OrderService.Extensions;

public static class OtelExtensions
{
    public const string ActivitySourceName = "OrderService.Orders";

    public static IServiceCollection AddOtelInstrumentation(this IServiceCollection services, IConfiguration configuration)
    {
        var serviceName = configuration["OTEL_SERVICE_NAME"] ?? "order-service";
        var serviceVersion = typeof(OtelExtensions).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
        var resourceBuilder = ResourceBuilder.CreateDefault().AddService(serviceName, serviceVersion: serviceVersion);

        services.AddSingleton<OrderBacklogSnapshot>();
        services.AddSingleton<IOrderMetrics, OrderMetrics>();
        services.AddHostedService<OrderBacklogSampler>();

        services
            .AddOpenTelemetry()
            .WithTracing(builder => builder
                .SetResourceBuilder(resourceBuilder)
                .AddSource(ActivitySourceName)
                .AddAspNetCoreInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                    options.Protocol = OtlpExportProtocol.Grpc;
                }))
            .WithMetrics(builder => builder
                .SetResourceBuilder(resourceBuilder)
                .AddMeter(OrderMetrics.MeterName)
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