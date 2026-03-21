using System.Diagnostics;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderService.Contracts;
using OrderService.Data;
using OrderService.Extensions;
using OrderService.Metrics;
using OrderService.Messaging;

var builder = WebApplication.CreateBuilder(args);
var postgresConnectionString = builder.Configuration["POSTGRES_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("POSTGRES_CONNECTION_STRING is required.");
var kafkaBootstrapServers = builder.Configuration["KAFKA_BOOTSTRAP_SERVERS"]
    ?? throw new InvalidOperationException("KAFKA_BOOTSTRAP_SERVERS is required.");

builder.WebHost.UseUrls("http://0.0.0.0:8080");
builder.Services.AddProblemDetails();
builder.Services.AddOtelInstrumentation(builder.Configuration);
builder.Services.AddDbContext<OrderDbContext>(options => options.UseNpgsql(postgresConnectionString));
builder.Services.AddSingleton(_ =>
    new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers = kafkaBootstrapServers,
        MessageTimeoutMs = 5000,
        SocketTimeoutMs = 5000
    }).Build());
builder.Services.AddSingleton<IKafkaOrderPublisher, KafkaOrderPublisher>();

var app = builder.Build();

await EnsureDatabaseSchemaAsync(app);

app.MapGet("/", () => Results.Ok(new
{
    service = "OrderService",
    status = "ready"
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy"
}));

app.MapPost("/orders", async (
    CreateOrderRequest request,
    OrderDbContext dbContext,
    IKafkaOrderPublisher publisher,
    IOrderMetrics metrics,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var stopwatch = Stopwatch.StartNew();
    var result = OrderCreateResults.Created;

    if (string.IsNullOrWhiteSpace(request.Description))
    {
        result = OrderCreateResults.ValidationFailed;
        metrics.RecordCreateResult(result, stopwatch.Elapsed);

        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["description"] = ["Description is required."]
        });
    }

    var order = new Order
    {
        Id = Guid.NewGuid(),
        Description = request.Description.Trim(),
        Status = OrderStatuses.PendingPublish,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    try
    {
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
    catch (Exception ex)
    {
        result = OrderCreateResults.PersistFailed;
        Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);

        logger.LogError(
            ex,
            "Failed to persist order {OrderId} {TraceId} {SpanId}",
            order.Id,
            Activity.Current?.TraceId.ToString(),
            Activity.Current?.SpanId.ToString());

        metrics.RecordCreateResult(result, stopwatch.Elapsed);

        return Results.Problem(
            title: "Failed to persist order.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    try
    {
        await publisher.PublishAsync(
            new OrderCreatedEvent(order.Id, order.Description, order.CreatedAtUtc),
            cancellationToken);
    }
    catch (Exception ex)
    {
        result = OrderCreateResults.PublishFailed;
        Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);

        order.Status = OrderStatuses.PublishFailed;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception updateEx)
        {
            logger.LogError(
                updateEx,
                "Failed to persist publish_failed status for order {OrderId} {TraceId} {SpanId}",
                order.Id,
                Activity.Current?.TraceId.ToString(),
                Activity.Current?.SpanId.ToString());
        }

        logger.LogError(
            ex,
            "Order publish failed {OrderId} {TraceId} {SpanId}",
            order.Id,
            Activity.Current?.TraceId.ToString(),
            Activity.Current?.SpanId.ToString());

        metrics.RecordCreateResult(result, stopwatch.Elapsed);

        return Results.Problem(
            title: "Failed to publish order event.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    order.Status = OrderStatuses.Published;
    order.PublishedAtUtc = DateTimeOffset.UtcNow;

    try
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }
    catch (Exception ex)
    {
        result = OrderCreateResults.StatusUpdateFailed;
        Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);

        logger.LogError(
            ex,
            "Order was published but status update failed {OrderId} {TraceId} {SpanId}",
            order.Id,
            Activity.Current?.TraceId.ToString(),
            Activity.Current?.SpanId.ToString());

        metrics.RecordCreateResult(result, stopwatch.Elapsed);

        return Results.Problem(
            title: "Order event was published but the persisted status could not be updated.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    logger.LogInformation(
        "Order created successfully {OrderId} {Status} {TraceId} {SpanId}",
        order.Id,
        order.Status,
        Activity.Current?.TraceId.ToString(),
        Activity.Current?.SpanId.ToString());

    metrics.RecordCreateResult(result, stopwatch.Elapsed);

    return Results.Created($"/orders/{order.Id}", OrderResponse.FromOrder(order));
});

app.MapGet("/orders/{id:guid}", async (
    Guid id,
    OrderDbContext dbContext,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var order = await dbContext.Orders
        .AsNoTracking()
        .SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

    if (order is null)
    {
        logger.LogInformation(
            "Order not found {OrderId} {TraceId} {SpanId}",
            id,
            Activity.Current?.TraceId.ToString(),
            Activity.Current?.SpanId.ToString());

        return Results.NotFound();
    }

    return Results.Ok(OrderResponse.FromOrder(order));
});

app.Run();

static async Task EnsureDatabaseSchemaAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();

    var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("OrderService.Startup");

    try
    {
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS orders (
                id uuid PRIMARY KEY,
                description text NOT NULL,
                status text NOT NULL,
                created_at_utc timestamp with time zone NOT NULL,
                published_at_utc timestamp with time zone NULL
            );

            CREATE INDEX IF NOT EXISTS ix_orders_status
                ON orders (status);
            """);
        logger.LogInformation("Order database schema ensured successfully.");
    }
    catch (PostgresException ex)
    {
        logger.LogError(ex, "Failed to create order service schema objects at startup.");
        throw;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to ensure order database schema at startup.");
        throw;
    }
    finally
    {
        await dbContext.Database.CloseConnectionAsync();
    }
}