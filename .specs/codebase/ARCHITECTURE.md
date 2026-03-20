# Architecture

**Padrão:** arquitetura distribuída orientada a eventos com uma API HTTP de entrada, dois workers assíncronos, mensageria Kafka, persistência em PostgreSQL e observabilidade centralizada via OpenTelemetry.

## Visão de Alto Nível

```text
Cliente HTTP
   |
   v
OrderService (HTTP + EF Core + Kafka producer)
   |
   | topic: orders
   v
Kafka
   |
   v
ProcessingWorker (Kafka consumer + HTTP client + Kafka producer)
   |
   | topic: notifications
   v
Kafka
   |
   v
NotificationWorker (Kafka consumer + EF Core)
   |
   v
PostgreSQL

Todos os serviços .NET
   |
   v
OpenTelemetry Collector
   |
   v
Grafana LGTM (Tempo + Loki + Prometheus + Grafana)
```

## Componentes Principais

### OrderService

**Localização:** `src/OrderService`

**Responsabilidade:** receber `POST /orders`, validar payload, persistir a ordem, publicar um evento Kafka e expor `GET /orders/{id}` para enriquecimento posterior.

**Elementos estruturais:**

- `Program.cs` concentra o bootstrap da API, DbContext, producer Kafka e endpoints minimal API.
- `Data/` modela a tabela `orders` e o mapeamento EF Core.
- `Messaging/KafkaOrderPublisher.cs` publica `OrderCreatedEvent` com propagação de contexto W3C.
- `Metrics/` expõe contadores, histograma de latência e gauge de backlog.

### ProcessingWorker

**Localização:** `src/ProcessingWorker`

**Responsabilidade:** consumir mensagens do topic `orders`, restaurar o contexto distribuído, consultar o OrderService via HTTP, validar o payload retornado e publicar `NotificationRequestedEvent` no topic `notifications`.

**Elementos estruturais:**

- `Worker.cs` implementa o loop de consumo e a lógica de processamento.
- `Clients/OrderServiceClient.cs` encapsula a chamada HTTP interna para `GET /orders/{id}`.
- `Messaging/KafkaNotificationPublisher.cs` publica o evento de notificação no Kafka.
- `Metrics/` mede throughput, duração do processamento e lag do consumidor.

### NotificationWorker

**Localização:** `src/NotificationWorker`

**Responsabilidade:** consumir mensagens do topic `notifications`, validar o contrato recebido, persistir o resultado final em `notification_results` e registrar o `trace_id` correlacionado.

**Elementos estruturais:**

- `Program.cs` configura Kafka, DbContext, instrumentação e bootstrap explícito de schema com `CREATE TABLE IF NOT EXISTS`.
- `Worker.cs` executa o loop de consumo, validação e persistência.
- `Data/` modela a tabela `notification_results` e seus índices.
- `Metrics/` mede persistência, falhas de consumo e lag do consumidor.

### Shared

**Localização:** `src/Shared/W3CTraceContext.cs`

**Responsabilidade:** encapsular extração e injeção de `traceparent` e `tracestate`, permitindo propagação de contexto por headers Kafka de forma consistente entre os três serviços.

## Fluxos Críticos

### Fluxo Feliz de Pedido

1. O cliente chama `POST /orders` no OrderService.
2. O OrderService valida `description`, cria uma entidade `Order` com status inicial `pending_publish` e persiste no PostgreSQL.
3. O serviço publica `OrderCreatedEvent` no topic `orders` com chave igual ao `orderId`.
4. O OrderService atualiza o pedido para status `published` e define `published_at_utc`.
5. O ProcessingWorker consome a mensagem, extrai o contexto distribuído do Kafka e abre um span consumidor.
6. O ProcessingWorker chama `GET /orders/{id}` no OrderService para enriquecer o fluxo com o estado persistido mais atual.
7. Após validação, o ProcessingWorker publica `NotificationRequestedEvent` no topic `notifications`.
8. O NotificationWorker consome a mensagem, valida o payload e persiste o resultado na tabela `notification_results`.
9. Todo o fluxo exporta traces, métricas e logs para o collector e depois para LGTM.

### Fluxo de Falha no OrderService

1. Falha ao persistir a ordem: a API registra erro, marca o span como erro, grava métricas com `persist_failed` e responde `500`.
2. Falha ao publicar no Kafka: a API tenta atualizar a ordem para `publish_failed`, grava métricas com `publish_failed` e responde `503`.
3. Falha ao atualizar status após publicação: o evento já foi emitido, mas o status persistido não é atualizado; a API retorna `500` e registra `status_update_failed`.

### Fluxo de Falha nos Workers

- Payload inválido no Kafka gera logging estruturado, status de erro no span e métricas específicas sem derrubar o loop do worker.
- Falhas HTTP durante enriquecimento no ProcessingWorker são classificadas como `not_found`, `http_error`, `timeout`, `network_error` ou `invalid_payload`.
- Falhas de persistência no NotificationWorker geram `persistence_failed` e mantêm o processo vivo.

## Observabilidade

### Instrumentação por Serviço

- Cada serviço define um `ActivitySource` próprio em `Extensions/OtelExtensions.cs`.
- O OrderService instrumenta ASP.NET Core, EF Core e HttpClient.
- O ProcessingWorker instrumenta HttpClient e spans manuais para Kafka.
- O NotificationWorker instrumenta EF Core, HttpClient e spans manuais para Kafka.

### Pipeline do Collector

**Localização:** `otelcol.yaml`

- Recebe OTLP gRPC e HTTP.
- Faz scrape das métricas internas do próprio collector via Prometheus.
- Aplica `memory_limiter`, `tail_sampling`, `span` e `batch`.
- Exporta traces, logs e metrics para a stack LGTM via OTLP HTTP.

### Estratégia de Sampling

**Localização:** `processors/sampling`

- `drop-health-checks.yaml` reduz o ruído de health checks bem-sucedidos.
- `keep-errors.yaml` preserva traces com erro.
- `sample-default.yaml` mantém o restante como catch-all.

## Organização do Código

**Abordagem:** modular por serviço, com organização interna por responsabilidade técnica.

Cada serviço segue praticamente a mesma divisão:

- `Contracts/` para DTOs e eventos.
- `Data/` para entidades e DbContexts quando há persistência.
- `Extensions/` para bootstrap transversal, especialmente OpenTelemetry.
- `Messaging/` para integração Kafka e propagação de trace context.
- `Metrics/` para medição de throughput, latência e lag.

## Fronteiras entre Módulos

- O OrderService é o único serviço exposto no host.
- O ProcessingWorker depende do OrderService apenas via HTTP interno e Kafka.
- O NotificationWorker depende apenas de Kafka e PostgreSQL.
- A observabilidade é externalizada em um collector único para simplificar o roteamento dos três sinais.
- O dashboard e os alertas são tratados como artefatos versionados de operação, não como configuração manual do ambiente.
