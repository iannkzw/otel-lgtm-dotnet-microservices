using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using ProcessingWorker;
using ProcessingWorker.Clients;
using ProcessingWorker.Extensions;
using ProcessingWorker.Messaging;

var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
var kafkaBootstrapServers = builder.Configuration["KAFKA_BOOTSTRAP_SERVERS"]
	?? throw new InvalidOperationException("KAFKA_BOOTSTRAP_SERVERS is required.");

builder.Services.AddOtelInstrumentation(builder.Configuration);
builder.Services.AddHttpClient<IOrderServiceClient, OrderServiceClient>((serviceProvider, client) =>
{
	var configuration = serviceProvider.GetRequiredService<IConfiguration>();
	var baseUrl = configuration["ORDER_SERVICE_BASE_URL"] ?? "http://order-service:8080/";

	if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
	{
		baseUrl += "/";
	}

	var timeoutSeconds = int.TryParse(configuration["ORDER_SERVICE_TIMEOUT_SECONDS"], out var parsedTimeout)
		? parsedTimeout
		: 5;

	client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
	client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});
builder.Services.AddSingleton<IConsumer<string, string>>(_ =>
	new ConsumerBuilder<string, string>(new ConsumerConfig
	{
		BootstrapServers = kafkaBootstrapServers,
		GroupId = builder.Configuration["KAFKA_GROUP_ID_PROCESSING"] ?? "processing-worker",
		AutoOffsetReset = AutoOffsetReset.Earliest,
		AllowAutoCreateTopics = true
	}).Build());
builder.Services.AddSingleton<IProducer<string, string>>(_ =>
	new ProducerBuilder<string, string>(new ProducerConfig
	{
		BootstrapServers = kafkaBootstrapServers,
		MessageTimeoutMs = 5000,
		SocketTimeoutMs = 5000
	}).Build());
builder.Services.AddSingleton<INotificationPublisher, KafkaNotificationPublisher>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();