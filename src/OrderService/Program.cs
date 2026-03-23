using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderService.Contracts;
using OrderService.Data;
using OrderService.Extensions;
using OrderService.Metrics;

var builder = WebApplication.CreateBuilder(args);
var postgresConnectionString = builder.Configuration["POSTGRES_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("POSTGRES_CONNECTION_STRING is required.");

builder.WebHost.UseUrls("http://0.0.0.0:8080");
builder.Services.AddProblemDetails();
builder.Services.AddOtelInstrumentation(builder.Configuration);
builder.Services.AddDbContext<OrderDbContext>(options => options.UseNpgsql(postgresConnectionString));

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
        Status = OrderStatuses.Published,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        PublishedAtUtc = DateTimeOffset.UtcNow
    };

    var outboxMessage = new OutboxMessage
    {
        Id = Guid.NewGuid(),
        OrderId = order.Id,
        Payload = JsonSerializer.Serialize(
            new OrderCreatedEvent(order.Id, order.Description, order.CreatedAtUtc)),
        AggregateType = "Order",
        EventType = "OrderCreated",
        IdempotencyKey = order.Id.ToString(),
        Traceparent = Activity.Current?.Id,
        Tracestate = Activity.Current?.TraceStateString,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    try
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Orders.Add(order);
        dbContext.OutboxMessages.Add(outboxMessage);
        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
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

            CREATE TABLE IF NOT EXISTS outbox_messages (
                id uuid PRIMARY KEY,
                order_id uuid NOT NULL,
                payload text NOT NULL,
                aggregate_type text NOT NULL DEFAULT 'Order',
                event_type text NOT NULL DEFAULT 'OrderCreated',
                idempotency_key text NOT NULL,
                traceparent text NULL,
                tracestate text NULL,
                created_at_utc timestamp with time zone NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS uix_outbox_messages_idempotency_key
                ON outbox_messages (idempotency_key);

            CREATE INDEX IF NOT EXISTS ix_outbox_messages_created
                ON outbox_messages (created_at_utc DESC);
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