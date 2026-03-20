# OrderService API e Persistência — Design

**Spec**: `.specs/features/order-service-api-persistencia/spec.md`
**Status**: Draft

---

## Architecture Overview

Esta feature expande apenas o `OrderService`, mantendo a baseline validada de M1 intacta. O serviço continuará em Minimal API, mas deixará de ser apenas um endpoint de readiness para assumir quatro responsabilidades novas dentro do mesmo trace de entrada:

1. validar e aceitar `POST /orders`;
2. persistir o pedido em PostgreSQL;
3. publicar o evento `orders` no Kafka com contexto W3C;
4. expor `GET /orders/{id}` para leitura futura pelo `ProcessingWorker`.

Fluxo esperado do `POST /orders`:

1. `AspNetCoreInstrumentation` cria o span root da request.
2. O endpoint valida o payload e cria um `Order` com `status = pending_publish`.
3. O `DbContext` persiste o pedido no PostgreSQL.
4. Um span manual de aplicação representa a publicação no Kafka.
5. O publisher injeta `traceparent` e `tracestate` nos headers da mensagem e publica no topic `orders`.
6. Em caso de sucesso, o serviço atualiza o registro para `status = published` e define `publishedAtUtc`.
7. Em caso de falha na publicação, o serviço atualiza o registro para `status = publish_failed`, marca o span com erro e responde `503`.

O `GET /orders/{id}` mantém deliberadamente um contrato simples e estável para que a subfeature seguinte do milestone consiga apenas consumir esta rota sem revisitar o design do `OrderService`.

---

## Design Decisions

### Persistir antes de publicar

**Decision**: O pedido será salvo primeiro com `status = pending_publish`, e só depois publicado no Kafka.

**Reason**: A feature precisa garantir que o `GET /orders/{id}` tenha fonte de verdade no banco e que falhas de publish sejam observáveis.

**Trade-off**: Sem outbox, ainda existe janela de inconsistência entre persistir e publicar. Para a PoC, isso é aceitável desde que o estado `publish_failed` fique explícito.

### Contrato mínimo e estável para o ProcessingWorker

**Decision**: `GET /orders/{id}` retornará exatamente os campos persistidos relevantes ao fluxo: `orderId`, `description`, `status`, `createdAtUtc`, `publishedAtUtc`.

**Reason**: O próximo passo de M2 depende desta rota para enriquecimento HTTP, e um contrato pequeno reduz churn entre features.

**Trade-off**: O modelo de domínio fica simplificado demais para um sistema real, mas adequado ao escopo da PoC.

### Bootstrap de schema no startup da aplicação

**Decision**: Para M2, o `OrderService` deverá aplicar bootstrap do schema no startup usando EF Core de forma automática e mínima.

**Reason**: A PoC precisa subir em ambiente limpo via Compose sem exigir etapa manual de criação da tabela `orders`.

**Trade-off**: Esta abordagem é menos rigorosa que um fluxo completo de migrations versionadas, mas reduz atrito para a demo.

---

## Existing Components to Reuse

| Component | Location | How to Reuse |
|-----------|----------|--------------|
| Bootstrap OTel do `OrderService` | `src/OrderService/Extensions/OtelExtensions.cs` | Expandir a configuração atual com `EntityFrameworkCoreInstrumentation` e `ActivitySource` manual para spans de aplicação/Kafka |
| Compose com Kafka/Postgres | `docker-compose.yaml` | Reusar `KAFKA_BOOTSTRAP_SERVERS` e `POSTGRES_CONNECTION_STRING` já definidos para o serviço |
| Baseline M1 no Tempo | `otelcol.yaml` + processors | Manter export OTLP atual sem alterar collector ou sampling |
| Minimal API atual | `src/OrderService/Program.cs` | Estender o arquivo existente com endpoints `POST /orders` e `GET /orders/{id}` |

---

## Components

### Order API Endpoints

- **Purpose**: Expor `POST /orders` e `GET /orders/{id}` em Minimal API.
- **Location**: `src/OrderService/Program.cs` ou método de mapeamento dedicado em pasta de endpoints.
- **Interfaces**:
  - `MapPost("/orders", ...)`
  - `MapGet("/orders/{id:guid}", ...)`
- **Dependencies**: DTOs, `OrderDbContext`, publisher Kafka, logger.

### OrderDbContext

- **Purpose**: Persistir e consultar pedidos no PostgreSQL.
- **Location**: `src/OrderService/Data/OrderDbContext.cs`
- **Entities**: `Order`
- **Dependencies**: `Npgsql.EntityFrameworkCore.PostgreSQL`

### Order Entity

- **Purpose**: Representar o estado persistido do pedido.
- **Location**: `src/OrderService/Data/Order.cs` ou `src/OrderService/Domain/Order.cs`
- **Fields**:
  - `Id: Guid`
  - `Description: string`
  - `Status: string`
  - `CreatedAtUtc: DateTimeOffset`
  - `PublishedAtUtc: DateTimeOffset?`

### KafkaOrderPublisher

- **Purpose**: Encapsular a publicação do evento de pedido no topic `orders`.
- **Location**: `src/OrderService/Messaging/KafkaOrderPublisher.cs`
- **Interfaces**:
  - `PublishAsync(OrderCreatedEvent orderEvent, CancellationToken cancellationToken)`
- **Dependencies**: `Confluent.Kafka`, `KafkaTracingHelper`, `ActivitySource`, logger.

### KafkaTracingHelper

- **Purpose**: Injetar o contexto W3C em headers Kafka de forma reutilizável para os workers futuros.
- **Location**: preferencialmente `src/Shared/Messaging/KafkaTracingHelper.cs` ou, se evitar novo projeto nesta etapa, uma pasta compartilhável no `OrderService` com código pronto para extração.
- **Interfaces**:
  - `Inject(Activity? activity, Headers headers)`
  - `Extract(Headers headers)` para reuse futuro em consumers
- **Dependencies**: `System.Diagnostics`, `Confluent.Kafka`

### OTel Extensions

- **Purpose**: Expandir a instrumentação do `OrderService` para spans de banco e spans manuais de aplicação.
- **Location**: `src/OrderService/Extensions/OtelExtensions.cs`
- **Changes**:
  - adicionar `AddEntityFrameworkCoreInstrumentation()`
  - adicionar `AddSource(...)` para o `ActivitySource` do domínio/aplicação
  - manter `AddAspNetCoreInstrumentation()` e `AddHttpClientInstrumentation()`

---

## Data Model

### PostgreSQL Table: orders

| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` | Primary key |
| `description` | `text` | Campo mock obrigatório |
| `status` | `text` | `pending_publish`, `published`, `publish_failed` |
| `created_at_utc` | `timestamp with time zone` | Timestamp de criação |
| `published_at_utc` | `timestamp with time zone null` | Preenchido apenas após publish bem-sucedido |

### API DTOs

**CreateOrderRequest**

```csharp
public sealed record CreateOrderRequest(string Description);
```

**OrderResponse**

```csharp
public sealed record OrderResponse(
    Guid OrderId,
    string Description,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PublishedAtUtc);
```

### Kafka Event Contract

```json
{
  "orderId": "3f5f6f3d-4d6f-4f19-b3de-1c7617a363a4",
  "description": "demo order",
  "createdAtUtc": "2026-03-19T18:30:00.0000000+00:00"
}
```

Observações:

- O body do evento permanece mínimo; a correlação distribuída depende dos headers Kafka, não do payload.
- O `orderId` será usado pelo `ProcessingWorker` para chamar `GET /orders/{id}`.

---

## Configuration

### Required Configuration

| Key | Source | Purpose |
|-----|--------|---------|
| `POSTGRES_CONNECTION_STRING` | já existe no compose | conexão do `OrderDbContext` |
| `KAFKA_BOOTSTRAP_SERVERS` | já existe no compose | producer Kafka |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | já existe no compose | export OTLP |
| `OTEL_SERVICE_NAME` | já existe no compose | `service.name` |

### Optional Defaults

| Key | Default | Purpose |
|-----|---------|---------|
| `KAFKA_TOPIC_ORDERS` | `orders` | permitir override sem mudar código |

---

## Observability Plan

### Traces

- `POST /orders` continua com span root automático do ASP.NET Core.
- EF Core gera spans de insert e update na tabela `orders`.
- Um span manual representa a publicação Kafka, por exemplo `kafka publish orders`.
- Falha de publish marca o span manual e a request com status de erro.

### Logs

- Logar criação do pedido com `orderId`, `status` e `TraceId`.
- Logar falha de persistência e de publish com exceção, `orderId` e identificadores do span atual.
- Logar `404` do `GET` com o `orderId` consultado para facilitar troubleshooting do worker futuro.

### Readiness for Future Features

- O helper de tracing Kafka já deve prever extração de headers para os consumers de `ProcessingWorker` e `NotificationWorker`.
- O contrato `GET /orders/{id}` deve permanecer estável quando o `ProcessingWorker` for implementado.
- O design não altera o collector nem o compose base de M1, apenas consome as integrações já aprovadas.

---

## Implementation Notes

- `Program.cs` deve continuar enxuto; registrar `DbContext`, producer Kafka e mapeamento dos endpoints sem concentrar lógica de negócio inline.
- O publisher Kafka deve ser tratado como infraestrutura isolada; o endpoint não deve manipular `Headers` Kafka diretamente.
- `publish_failed` é um estado explícito de observabilidade da PoC, não um mecanismo completo de recuperação.