# Transaction Outbox — CDC + Debezium Outbox Event Router Tasks

**Design**: `.specs/features/transaction-outbox/design.md`
**Status**: Draft

---

## Execution Plan

### Phase 1: Domain — Foundation (Sequential)

```
T1 → T2 → T3
```

Entidade + DbContext + DDL. Base para T4 e T5.

### Phase 2: Application Code (T4 e T5 paralelos após Phase 1)

```
     ┌→ T4 ─┐
T3 ──┤       ├──→ T6
     └→ T5 ─┘
```

T4 refatora o `POST /orders`; T5 cria o schema no banco. T6 depende de ambos.

### Phase 3: Infrastructure (Sequential após T3)

```
T3 → T6 → T7 → T8
```

T6 config do conector Debezium, T7 docker-compose (Postgres WAL + kafka-connect + connector-init), T8 valida o stack completo.

---

## Task Breakdown

---

### T1: Criar entidade `OutboxMessage`

**What**: Criar a entidade `OutboxMessage` com colunas: `Id`, `OrderId`, `Payload`, `AggregateType`, `EventType`, `IdempotencyKey`, `Traceparent`, `Tracestate`, `CreatedAtUtc`
**Where**: `src/OrderService/Data/OutboxMessage.cs` (arquivo novo)
**Depends on**: Nada
**Reuses**: Padrão de `src/OrderService/Data/Order.cs`

**Implementation**:
```csharp
namespace OrderService.Data;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string AggregateType { get; set; } = "Order";
    public string EventType { get; set; } = "OrderCreated";
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? Traceparent { get; set; }
    public string? Tracestate { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
```

**Verification**: `dotnet build` passa sem erros.

---

### T2: Atualizar `OrderDbContext` — adicionar `OutboxMessages`

**What**: Adicionar `DbSet<OutboxMessage>` e mapeamento fluent da tabela `outbox_messages` em `OnModelCreating`
**Where**: `src/OrderService/Data/OrderDbContext.cs`
**Depends on**: T1 (`OutboxMessage`)
**Reuses**: Padrão fluent API idêntico ao mapeamento de `Order`

**Changes**:
1. Adicionar property: `public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();`
2. Bloco em `OnModelCreating`:
   ```csharp
   var outbox = modelBuilder.Entity<OutboxMessage>();
   outbox.ToTable("outbox_messages");
   outbox.HasKey(e => e.Id);
   outbox.Property(e => e.Id).HasColumnName("id");
   outbox.Property(e => e.OrderId).HasColumnName("order_id");
   outbox.Property(e => e.Payload).HasColumnName("payload").IsRequired();
   outbox.Property(e => e.AggregateType).HasColumnName("aggregate_type").IsRequired();
   outbox.Property(e => e.EventType).HasColumnName("event_type").IsRequired();
   outbox.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").IsRequired();
   outbox.Property(e => e.Traceparent).HasColumnName("traceparent");
   outbox.Property(e => e.Tracestate).HasColumnName("tracestate");
   outbox.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
   outbox.HasIndex(e => e.IdempotencyKey).IsUnique();
   ```

**Verification**: `dotnet build` passa.

---

### T3: Atualizar `EnsureDatabaseSchemaAsync` — DDL de `outbox_messages`

**What**: Adicionar DDL de criação da tabela `outbox_messages` com UNIQUE INDEX em `idempotency_key` ao bloco `ExecuteSqlRawAsync` existente
**Where**: `src/OrderService/Program.cs` — função `EnsureDatabaseSchemaAsync`
**Depends on**: T2
**Reuses**: Bloco `ExecuteSqlRawAsync` existente

**DDL a adicionar** (após DDL de `orders`):
```sql
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
```

**Verification**: `docker compose up postgres order-service` → conectar no Postgres → `\d outbox_messages` mostra a tabela com índices.

---

### T4: Refatorar `POST /orders` — transação atômica sem Kafka

**What**: Remover `IKafkaOrderPublisher` e `IProducer` do endpoint; salvar `Order` (status=`published`) + `OutboxMessage` em uma única transação EF Core explícita (`IDbContextTransaction`)
**Where**: `src/OrderService/Program.cs`
**Depends on**: T2 (`OutboxMessage`)
**Reuses**: `JsonSerializer`, `OrderDbContext`, `OrderStatuses`, `OrderCreatedEvent`, `Activity.Current`

**Changes**:
1. Remover `IKafkaOrderPublisher publisher` dos parâmetros do handler
2. Remover o registro de `IProducer<string, string>` e `IKafkaOrderPublisher` do DI em `builder.Services`
3. Substituir os três blocos de try/catch (persist → publish → updateStatus) por um único bloco:

```csharp
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
    result = OrderCreateResults.Created;
}
catch (Exception ex)
{
    result = OrderCreateResults.PersistFailed;
    Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
    logger.LogError(ex, "Failed to persist order {OrderId} ...", order.Id, ...);
    metrics.RecordCreateResult(result, stopwatch.Elapsed);
    return Results.Problem(title: "Failed to persist order.", statusCode: 500);
}
```

4. Remover `OrderCreateResults.PublishFailed` e `OrderCreateResults.StatusUpdateFailed` do `OrderMetrics` (não são mais alcançáveis)

**Verification**: `POST /orders` retorna `201`; banco tem `orders.status = 'published'` e `outbox_messages` com `traceparent` preenchido; nenhuma mensagem Kafka ainda.

---

### T5: Criar config do conector Debezium (Outbox Event Router)

**What**: Criar arquivo JSON de configuração do conector Debezium PostgreSQL com o SMT `EventRouter` configurado para propagar `traceparent` e `tracestate` como headers Kafka
**Where**: `tools/debezium/order-outbox-connector.json` (arquivo novo)
**Depends on**: T1 (nomes das colunas da outbox)
**Reuses**: Nada novo — config pura

**Content**:
```json
{
  "name": "order-outbox-connector",
  "config": {
    "connector.class": "io.debezium.connector.postgresql.PostgresConnector",
    "database.hostname": "postgres",
    "database.port": "5432",
    "database.user": "poc",
    "database.password": "poc",
    "database.dbname": "otelpoc",
    "plugin.name": "pgoutput",
    "publication.autocreate.mode": "filtered",
    "table.include.list": "public.outbox_messages",
    "topic.prefix": "dbz",
    "transforms": "outbox",
    "transforms.outbox.type": "io.debezium.transforms.outbox.EventRouter",
    "transforms.outbox.table.field.event.id": "id",
    "transforms.outbox.table.field.event.key": "order_id",
    "transforms.outbox.table.field.event.payload": "payload",
    "transforms.outbox.route.by.field": "aggregate_type",
    "transforms.outbox.route.topic.replacement": "orders",
    "transforms.outbox.table.fields.additional.placement": "traceparent:header:traceparent,tracestate:header:tracestate"
  }
}
```

**Key decisions**:
- `route.topic.replacement = "orders"` → publica no mesmo topic que o `KafkaOrderPublisher` usava
- `table.field.event.key = "order_id"` → mantém o `order_id` como Kafka message key (compatível com o `ProcessingWorker`)
- `table.fields.additional.placement` → propaga `traceparent` e `tracestate` das colunas da outbox para headers da mensagem Kafka → `KafkaTracingHelper.Extract` do worker lê exatamente esses header names

**Verification**: Arquivo JSON é válido; `curl -s http://localhost:8083/connectors/order-outbox-connector` retorna a config após o docker compose subir.

---

### T6: Atualizar `docker-compose.yaml` — Postgres WAL + kafka-connect + connector-init

**What**: Adicionar:
1. Parâmetros de WAL no container `postgres`
2. Novo serviço `kafka-connect` (Debezium)
3. Novo serviço `connector-init` que registra o conector via REST API
**Where**: `docker-compose.yaml`
**Depends on**: T5 (connector JSON existente)
**Reuses**: Serviços existentes como referência de padrão

**Changes**:

1. Postgres — adicionar `command`:
```yaml
postgres:
  image: postgres:16-alpine
  command:
    - "postgres"
    - "-c"
    - "wal_level=logical"
    - "-c"
    - "max_replication_slots=5"
    - "-c"
    - "max_wal_senders=5"
  environment:
    ...
```

2. Novo serviço `kafka-connect`:
```yaml
kafka-connect:
  image: debezium/connect:3.0
  ports:
    - "8083:8083"
  environment:
    BOOTSTRAP_SERVERS: kafka:9092
    GROUP_ID: "1"
    CONFIG_STORAGE_TOPIC: connect_configs
    OFFSET_STORAGE_TOPIC: connect_offsets
    STATUS_STORAGE_TOPIC: connect_statuses
  depends_on:
    kafka:
      condition: service_healthy
    postgres:
      condition: service_healthy
  healthcheck:
    test: ["CMD-SHELL", "curl -sf http://localhost:8083/connectors || exit 1"]
    interval: 10s
    timeout: 5s
    retries: 10
    start_period: 30s
  networks:
    - otel-demo
```

3. Novo serviço `connector-init`:
```yaml
connector-init:
  image: curlimages/curl:latest
  depends_on:
    kafka-connect:
      condition: service_healthy
  volumes:
    - ./tools/debezium/order-outbox-connector.json:/connector.json:ro
  command: >
    sh -c "curl -sf -X POST http://kafka-connect:8083/connectors
    -H 'Content-Type: application/json'
    -d @/connector.json &&
    echo 'Debezium connector registered successfully'"
  networks:
    - otel-demo
```

4. Atualizar `order-service` `depends_on` para incluir `kafka-connect` com `condition: service_healthy` (o OrderService deve iniciar após o conector estar pronto)

**Verification**: `docker compose up` → logs do `connector-init` mostram `"Debezium connector registered successfully"`. `curl http://localhost:8083/connectors/order-outbox-connector/status` mostra `"state": "RUNNING"`.

---

### T7: Smoke test end-to-end e validação do trace

**What**: Validar o fluxo completo: `POST /orders` → outbox → Debezium → Kafka → ProcessingWorker → `GET /orders/{id}` com trace contínuo no Grafana
**Where**: Workspace root
**Depends on**: T1–T6 concluídos

**Steps**:
1. Executar task `build-solution-sdk10` → deve passar sem erros
2. `docker compose up --build`
3. Aguardar todos os healthchecks: `kafka-connect`, `connector-init`, `order-service`, `processing-worker`
4. `POST http://localhost:8080/orders` com body `{ "description": "cdc smoke test" }`
5. Verificar no Postgres:
   - `SELECT status, published_at_utc FROM orders WHERE ...` → `published`, data preenchida
   - `SELECT traceparent, order_id FROM outbox_messages WHERE ...` → `traceparent` não nulo
6. Aguardar ~1-2s; verificar no Kafka UI (`http://localhost:8085`):
   - Topic `orders` tem a mensagem
   - Header `traceparent` está presente na mensagem
7. Verificar no Grafana (`http://localhost:3000`):
   - Traces → buscar pelo `traceId` do `POST /orders`
   - Waterfall deve mostrar: `POST /orders` → `kafka consume orders` → `GET /orders/{id}`
   - Span `order_not_published` NÃO deve aparecer

**Verification**: Todos os passos acima passam. Race condition eliminada. Trace end-to-end visível no Grafana.

---

## Dependency Matrix

| Task | Depende de | Pode paralelizar com |
|---|---|---|
| T1 | — | — |
| T2 | T1 | — |
| T3 | T2 | T4* |
| T4 | T2 | T3*, T5* |
| T5 | T1 | T3*, T4* |
| T6 | T5 | — |
| T7 | T1–T6 | — |

\* paralelo possível; T3 e T4 editam o mesmo arquivo `Program.cs` — não editar simultaneamente

**Design**: `.specs/features/transaction-outbox/design.md`
**Status**: Draft

---

## Execution Plan

### Phase 1: Domain — Foundation (Sequential)

```
T1 → T2 → T3
```

Cria as entidades e atualiza o contexto EF Core. Todos os demais tasks dependem de T3.

### Phase 2: Infrastructure (Sequential após Phase 1)

```
T3 → T4 → T5
```

T4 cria o schema no banco; T5 refatora o endpoint `POST /orders`.

### Phase 3: Relay Worker (Sequential após T3, paralelo T6/T7)

```
T3 ──┬→ T6 ─┐
     └→ T7 ─┴──→ T8 → T9
```

T6 (worker) e T7 (métricas) podem ser desenvolvidos em paralelo pois dependem apenas de T3.  
T8 registra os serviços no DI (depende de T6 e T7).  
T9 adiciona a instrumentação OTel ao worker (depende de T6 e T7).

### Phase 4: Validação (Sequential)

```
T9 → T10
```

T10 verifica build Docker e comportamento end-to-end.

---

## Task Breakdown

---

### T1: Criar `OutboxStatus` — constantes de status

**What**: Criar classe estática com constantes `Pending`, `Published`, `Failed` para `outbox_messages.status`
**Where**: `src/OrderService/Data/OutboxStatus.cs` (arquivo novo)
**Depends on**: Nada
**Reuses**: Seguir padrão exato de `src/OrderService/Data/OrderStatuses.cs`

**Implementation**:
```csharp
namespace OrderService.Data;

public static class OutboxStatus
{
    public const string Pending = "pending";
    public const string Published = "published";
    public const string Failed = "failed";
}
```

**Verification**: Arquivo compila sem erros (`dotnet build`).

---

### T2: Criar entidade `OutboxMessage`

**What**: Criar entidade `OutboxMessage` com propriedades: `Id`, `OrderId`, `Payload`, `Status`, `IdempotencyKey`, `CreatedAtUtc`, `PublishedAtUtc`, `ErrorMessage`
**Where**: `src/OrderService/Data/OutboxMessage.cs` (arquivo novo)
**Depends on**: T1 (`OutboxStatus`)
**Reuses**: Seguir padrão de `src/OrderService/Data/Order.cs`

**Implementation**:
```csharp
namespace OrderService.Data;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string Status { get; set; } = OutboxStatus.Pending;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
}
```

**Verification**: Arquivo compila sem erros.

---

### T3: Atualizar `OrderDbContext` — adicionar `OutboxMessages`

**What**: Adicionar `DbSet<OutboxMessage>` e mapeamento fluent da tabela `outbox_messages` em `OnModelCreating`
**Where**: `src/OrderService/Data/OrderDbContext.cs`
**Depends on**: T2 (`OutboxMessage`)
**Reuses**: Padrão fluent API idêntico ao mapeamento de `Order` já existente

**Changes**:
1. Adicionar property: `public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();`
2. Adicionar bloco em `OnModelCreating`:
   - `ToTable("outbox_messages")`
   - `HasKey(e => e.Id)` → coluna `id`
   - Mapear todas as colunas: `order_id`, `payload`, `status`, `idempotency_key`, `created_at_utc`, `published_at_utc`, `error_message`
   - `HasIndex(e => new { e.Status, e.CreatedAtUtc })` → índice `ix_outbox_messages_status`
   - `HasIndex(e => e.IdempotencyKey).IsUnique()` → constraint `uix_outbox_messages_idempotency_key`

**Verification**: `dotnet build` passa. Nenhuma alteração em migration (schema criado via raw SQL no startup).

---

### T4: Atualizar `EnsureDatabaseSchemaAsync` — DDL de `outbox_messages`

**What**: Adicionar DDL de criação da tabela `outbox_messages`, índice parcial em `status='pending'` e `UNIQUE INDEX` em `idempotency_key` ao bloco `ExecuteSqlRawAsync` existente
**Where**: `src/OrderService/Program.cs` — função `EnsureDatabaseSchemaAsync`
**Depends on**: T3
**Reuses**: Bloco `ExecuteSqlRawAsync` existente na mesma função

**DDL a adicionar** (após DDL de `orders`):
```sql
CREATE TABLE IF NOT EXISTS outbox_messages (
    id uuid PRIMARY KEY,
    order_id uuid NOT NULL,
    payload text NOT NULL,
    status text NOT NULL DEFAULT 'pending',
    idempotency_key text NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    published_at_utc timestamp with time zone NULL,
    error_message text NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS uix_outbox_messages_idempotency_key
    ON outbox_messages (idempotency_key);

CREATE INDEX IF NOT EXISTS ix_outbox_messages_status_created
    ON outbox_messages (status, created_at_utc)
    WHERE status = 'pending';
```

**Verification**: Subir `docker compose up postgres` e verificar que as tabelas e índices existem após o startup do `order-service`.

---

### T5: Refatorar `POST /orders` — salvar atomicamente, sem Kafka

**What**: Remover a chamada a `IKafkaOrderPublisher` do endpoint `POST /orders` e substituir pelos blocos de persist Kafka + status update por: serializar `OrderCreatedEvent`, criar `OutboxMessage`, `dbContext.Add(outboxMessage)` e um único `await dbContext.SaveChangesAsync` que persiste `Order` + `OutboxMessage` na mesma transação EF Core implícita
**Where**: `src/OrderService/Program.cs` — handler do `app.MapPost("/orders", ...)`
**Depends on**: T3 (`OutboxMessage`, `OutboxStatus`)
**Reuses**: `JsonSerializer`, `OrderDbContext`, `OrderStatuses`, `OrderCreatedEvent`

**Changes**:
1. Remover parâmetro `IKafkaOrderPublisher publisher` do handler
2. Remover todo o bloco `try { await publisher.PublishAsync(...) } catch {...}` e o `SaveChangesAsync` de status update
3. Adicionar após o primeiro `SaveChangesAsync` (insert do `Order`):

```csharp
// Serializar evento para payload da outbox
var outboxPayload = JsonSerializer.Serialize(
    new OrderCreatedEvent(order.Id, order.Description, order.CreatedAtUtc));

var outboxMessage = new OutboxMessage
{
    Id = Guid.NewGuid(),
    OrderId = order.Id,
    Payload = outboxPayload,
    Status = OutboxStatus.Pending,
    IdempotencyKey = order.Id.ToString(),
    CreatedAtUtc = DateTimeOffset.UtcNow
};

dbContext.OutboxMessages.Add(outboxMessage);
await dbContext.SaveChangesAsync(cancellationToken);
```

4. `result = OrderCreateResults.Created` e retornar `201 Created` normalmente

> **Nota**: O EF Core não usa transação explícita aqui porque os dois `SaveChangesAsync` são separados. Para garantir atomicidade real entre `orders` e `outbox_messages`, encapsular ambas em uma `IDbContextTransaction` explícita:
> ```csharp
> await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
> // INSERT Order
> // INSERT OutboxMessage
> await dbContext.SaveChangesAsync(cancellationToken);
> await tx.CommitAsync(cancellationToken);
> ```

**Verification**: `POST /orders` retorna `201`; banco tem `orders.status = 'pending_publish'` e `outbox_messages.status = 'pending'`; Kafka topic `orders` NÃO contém a mensagem ainda.

---

### T6: Criar `OutboxRelayWorker` — BackgroundService de polling e relay

**What**: Criar `OutboxRelayWorker : BackgroundService` que a cada `pollInterval` ms busca mensagens `pending`, publica no Kafka e commita estado final (`published` ou `failed`) atomicamente
**Where**: `src/OrderService/Messaging/OutboxRelayWorker.cs` (arquivo novo)
**Depends on**: T3 (`OutboxMessage`, `OrderDbContext`), T1 (`OutboxStatus`)
**Reuses**: `IKafkaOrderPublisher`, `OrderDbContext`, `OrderStatuses`, `OutboxStatus`, `JsonSerializer`, `ILogger`

**Pseudocode do loop**:
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        await ProcessPendingMessagesAsync(stoppingToken);
        await Task.Delay(_pollInterval, stoppingToken);
    }
}

private async Task ProcessPendingMessagesAsync(CancellationToken ct)
{
    await using var scope = _scopeFactory.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

    var pending = await db.OutboxMessages
        .Where(m => m.Status == OutboxStatus.Pending)
        .OrderBy(m => m.CreatedAtUtc)
        .Take(_batchSize)
        .ToListAsync(ct);

    foreach (var message in pending)
        await RelayMessageAsync(db, message, ct);
}

private async Task RelayMessageAsync(OrderDbContext db, OutboxMessage message, CancellationToken ct)
{
    try
    {
        var orderEvent = JsonSerializer.Deserialize<OrderCreatedEvent>(message.Payload, _serializerOptions)!;
        await _publisher.PublishAsync(orderEvent, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        message.Status = OutboxStatus.Published;
        message.PublishedAtUtc = DateTimeOffset.UtcNow;

        var order = await db.Orders.FindAsync([message.OrderId], ct);
        if (order is not null)
        {
            order.Status = OrderStatuses.Published;
            order.PublishedAtUtc = message.PublishedAtUtc;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
    catch (Exception ex)
    {
        message.Status = OutboxStatus.Failed;
        message.ErrorMessage = ex.Message;
        await db.SaveChangesAsync(ct);
        _logger.LogError(ex, "Relay failed for outbox message {MessageId} order {OrderId}", message.Id, message.OrderId);
    }
}
```

**Constructor**: `(IServiceScopeFactory scopeFactory, IKafkaOrderPublisher publisher, IConfiguration config, ILogger<OutboxRelayWorker> logger, IOrderMetrics metrics)`

**Verification**: Após `POST /orders`, dentro de ~500ms: `orders.status = 'published'`, `outbox_messages.status = 'published'`, mensagem no Kafka topic `orders`.

---

### T7: Adicionar métricas do relay a `OrderMetrics`

**What**: Adicionar `Counter<long>` `outbox.relay.published.total`, `Counter<long>` `outbox.relay.failed.total` e `Histogram<double>` `outbox.relay.duration` ao `OrderMetrics`; adicionar método `RecordRelayResult(string result, TimeSpan duration)`
**Where**: `src/OrderService/Metrics/OrderMetrics.cs`
**Depends on**: T1 (constantes `OutboxStatus` para validação do resultado)
**Reuses**: `_meter` existente, padrão de `RecordCreateResult`

**New constants** (classe interna `OutboxRelayResults`):
```csharp
public static class OutboxRelayResults
{
    public const string Published = "published";
    public const string Failed = "failed";
}
```

**New metrics**:
```csharp
private readonly Counter<long> _outboxRelayPublishedCounter;   // outbox.relay.published.total
private readonly Counter<long> _outboxRelayFailedCounter;      // outbox.relay.failed.total
private readonly Histogram<double> _outboxRelayDurationHistogram; // outbox.relay.duration (ms)
```

**New method**:
```csharp
public void RecordRelayResult(string result, TimeSpan duration);
```

**Interface update**: Adicionar `void RecordRelayResult(string result, TimeSpan duration)` em `IOrderMetrics`

**Verification**: `dotnet build` passa. Métricas visíveis no Grafana após relay processar mensagens.

---

### T8: Registrar `OutboxRelayWorker` no DI e adicionar configuração em `appsettings.json`

**What**: Registrar `OutboxRelayWorker` como `HostedService` em `Program.cs`; adicionar seção `OutboxRelay` em `appsettings.json` com `PollIntervalMs: 500` e `BatchSize: 10`
**Where**: 
  - `src/OrderService/Program.cs` (registration)
  - `src/OrderService/appsettings.json` (config)
  - `src/OrderService/appsettings.Development.json` (config de dev)
**Depends on**: T6 (`OutboxRelayWorker`), T7 (`IOrderMetrics` atualizado)
**Reuses**: `builder.Services.AddHostedService<>()` padrão; `IConfiguration` já injetado

**Program.cs change**:
```csharp
builder.Services.AddHostedService<OutboxRelayWorker>();
```

**appsettings.json addition**:
```json
"OutboxRelay": {
  "PollIntervalMs": 500,
  "BatchSize": 10
}
```

**Verification**: `OutboxRelayWorker` aparece nos logs de startup: `"Outbox relay worker started. PollIntervalMs=500, BatchSize=10"`.

---

### T9: Adicionar instrumentação OTel ao `OutboxRelayWorker`

**What**: Adicionar span `outbox relay` para cada mensagem processada, com tags `outbox.order_id`, `outbox.message_id`, `messaging.destination.name`; conectar ao `Activity.Current` da publicação Kafka para span contínuo
**Where**: `src/OrderService/Messaging/OutboxRelayWorker.cs`
**Depends on**: T6 (worker existente), T7 (métricas)
**Reuses**: `ActivitySource` via `ActivitySourceHolder` (padrão do `KafkaOrderPublisher`); `ActivityStatusCode`

**Changes em `RelayMessageAsync`**:
```csharp
using var activity = ActivitySourceHolder.ActivitySource
    .StartActivity("outbox relay", ActivityKind.Internal);

activity?.SetTag("outbox.message_id", message.Id.ToString());
activity?.SetTag("outbox.order_id", message.OrderId.ToString());
activity?.SetTag("messaging.destination.name", _topic);

// em sucesso:
// nada extra — o span de kafka publish já está linkado

// em falha:
activity?.AddException(ex);
activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
```

- Registrar duração via `Stopwatch` e chamar `metrics.RecordRelayResult(result, stopwatch.Elapsed)`

**Verification**: No Grafana → Traces, pedido criado via `POST /orders` deve ter a cadeia:
`http POST /orders` → `outbox relay` → `kafka publish orders` → `kafka consume orders` (ProcessingWorker).

---

### T10: Validar build Docker e smoke test end-to-end

**What**: Executar build via task `build-solution-sdk10` do workspace e validar comportamento com `docker compose up`
**Where**: Workspace root
**Depends on**: T1–T9 concluídos
**Reuses**: Task `build-solution-sdk10` existente no VS Code

**Steps**:
1. Executar task `build-solution-sdk10` → deve passar sem erros
2. `docker compose up --build order-service processing-worker`
3. `POST http://localhost:8080/orders` com body `{ "description": "smoke test" }`
4. Aguardar ~1s; verificar no Postgres:
   - `SELECT status FROM orders WHERE ...` → `published`
   - `SELECT status FROM outbox_messages WHERE ...` → `published`
5. Verificar no Kafka UI (`http://localhost:8085`) que a mensagem chegou no topic `orders`
6. Verificar no Grafana (`http://localhost:3000`) que nenhum span `order_not_published` foi gerado
7. Verificar que o trace distribuído mostra `outbox relay` entre `POST /orders` e `kafka consume orders`

**Verification**: Todos os passos acima passam sem erro. Span `order_not_published` não deve aparecer.

---

## Dependency Matrix

| Task | Depende de | Pode paralelizar com |
|---|---|---|
| T1 | — | T2* |
| T2 | T1 | — |
| T3 | T2 | — |
| T4 | T3 | T5* |
| T5 | T3 | T4*, T6*, T7* |
| T6 | T3 | T7 |
| T7 | T1 | T6 |
| T8 | T6, T7 | — |
| T9 | T6, T7 | — |
| T10 | T1–T9 | — |

\* paralelo possível com cuidado (mesmos arquivos não podem ser editados simultaneamente)
