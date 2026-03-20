using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Extensions.Hosting;
using NotificationWorker;
using NotificationWorker.Data;
using NotificationWorker.Extensions;
using NotificationWorker.Metrics;

var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
var kafkaBootstrapServers = builder.Configuration["KAFKA_BOOTSTRAP_SERVERS"]
	?? throw new InvalidOperationException("KAFKA_BOOTSTRAP_SERVERS is required.");
var postgresConnectionString = builder.Configuration["POSTGRES_CONNECTION_STRING"]
	?? throw new InvalidOperationException("POSTGRES_CONNECTION_STRING is required.");

builder.Services.AddOtelInstrumentation(builder.Configuration);
builder.Services.AddDbContext<NotificationDbContext>(options => options.UseNpgsql(postgresConnectionString));
builder.Services.AddSingleton<IConsumer<string, string>>(serviceProvider =>
{
	var logger = serviceProvider.GetRequiredService<ILogger<Worker>>();
	var metrics = serviceProvider.GetRequiredService<INotificationMetrics>();

	return new ConsumerBuilder<string, string>(new ConsumerConfig
	{
		BootstrapServers = kafkaBootstrapServers,
		GroupId = builder.Configuration["KAFKA_GROUP_ID_NOTIFICATION"] ?? "notification-worker",
		AutoOffsetReset = AutoOffsetReset.Earliest,
		AllowAutoCreateTopics = true
	})
		.SetErrorHandler((_, error) =>
		{
			metrics.RecordConsumeFailure($"{error.Code}:{error.Reason}");

			logger.LogError(
				"Kafka consumer error Classification={Classification} Reason={Reason} Code={Code} IsFatal={IsFatal}",
				"consume_failed",
				error.Reason,
				error.Code,
				error.IsFatal);
		})
		.Build();
});
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await EnsureDatabaseSchemaAsync(host.Services);
host.Run();

static async Task EnsureDatabaseSchemaAsync(IServiceProvider services)
{
	using var scope = services.CreateScope();

	var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
	var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("NotificationWorker.Startup");

	try
	{
		await dbContext.Database.OpenConnectionAsync();
		await dbContext.Database.ExecuteSqlRawAsync(
			"""
			CREATE TABLE IF NOT EXISTS notification_results (
				id uuid PRIMARY KEY,
				order_id uuid NOT NULL,
				description text NOT NULL,
				status text NOT NULL,
				created_at_utc timestamp with time zone NOT NULL,
				published_at_utc timestamp with time zone NOT NULL,
				processed_at_utc timestamp with time zone NOT NULL,
				persisted_at_utc timestamp with time zone NOT NULL,
				trace_id text NOT NULL
			);

			CREATE INDEX IF NOT EXISTS ix_notification_results_order_id
				ON notification_results (order_id);

			CREATE INDEX IF NOT EXISTS ix_notification_results_trace_id
				ON notification_results (trace_id);
			""");
		logger.LogInformation("Notification worker database schema ensured successfully.");
	}
	catch (PostgresException ex)
	{
		logger.LogError(ex, "Failed to create notification worker schema objects at startup.");
	}
	catch (Exception ex)
	{
		logger.LogError(ex, "Failed to ensure notification worker database schema at startup.");
	}
	finally
	{
		await dbContext.Database.CloseConnectionAsync();
	}
}