# External Integrations

## OrderService HTTP API

**Serviço:** `order-service`

**Porta exposta no host:** `localhost:8080`

### Endpoints

- `GET /` retorna status simples do serviço.
- `GET /health` retorna health básico usado também em verificações operacionais.
- `POST /orders` cria um pedido a partir de `CreateOrderRequest`.
- `GET /orders/{id:guid}` retorna `OrderResponse` para consumo pelo ProcessingWorker.

**Consumidores principais:**

- usuário/host local para a demo;
- `ProcessingWorker` via `ORDER_SERVICE_BASE_URL=http://order-service:8080/`.

## Kafka

**Serviço:** `confluentinc/cp-kafka:7.5.0`

**Endpoint interno:** `kafka:9092`

**Configuração relevante:** `KAFKA_AUTO_CREATE_TOPICS_ENABLE=true`

### Topics

#### `orders`

- **Producer:** OrderService
- **Consumer:** ProcessingWorker
- **Chave:** `OrderId` como string
- **Payload:** `OrderCreatedEvent`

#### `notifications`

- **Producer:** ProcessingWorker
- **Consumer:** NotificationWorker
- **Chave:** `OrderId` como string
- **Payload:** `NotificationRequestedEvent`

### Propagação de Contexto

- Os serviços injetam `traceparent` e `tracestate` nos headers Kafka.
- A restauração do contexto ocorre antes da abertura dos spans de consumo.
- A implementação reaproveita a lógica compartilhada de W3C trace context.

## PostgreSQL

**Serviço:** `postgres:16-alpine`

**Database:** `otelpoc`

**Credenciais locais:** `poc` / `poc`

### Tabela `orders`

- usada pelo OrderService;
- schema mapeado por `OrderDbContext`;
- colunas: `id`, `description`, `status`, `created_at_utc`, `published_at_utc`.

### Tabela `notification_results`

- usada pelo NotificationWorker;
- schema mapeado por `NotificationDbContext` e reforçado por DDL explícito no startup;
- colunas: `id`, `order_id`, `description`, `status`, `created_at_utc`, `published_at_utc`, `processed_at_utc`, `persisted_at_utc`, `trace_id`;
- índices: `ix_notification_results_order_id`, `ix_notification_results_trace_id`.

## OpenTelemetry Collector

**Serviço:** `otel/opentelemetry-collector-contrib:latest`

### Entradas

- OTLP gRPC em `4317`
- OTLP HTTP em `4318`
- scrape Prometheus das métricas internas do próprio collector em `8888`

### Saídas

- traces para `http://lgtm:4318/v1/traces`
- logs para `http://lgtm:4318/v1/logs`
- métricas para `http://lgtm:4318/v1/metrics`

### Processamento

- `memory_limiter`
- `tail_sampling`
- `span`
- `batch`

As policies de sampling ficam em `processors/sampling/` e são carregadas modularmente.

## Grafana LGTM

**Serviço:** `grafana/otel-lgtm:latest`

**Porta host:** `localhost:3000`

**Capacidades usadas pela PoC:**

- Grafana UI
- Tempo para traces
- Loki para logs
- Prometheus para métricas

### Provisioning Versionado

- dashboard: `grafana/dashboards/otel-poc-overview.json`
- dashboards provisioning: `grafana/provisioning/dashboards/otel-poc-dashboards.yaml`
- alert rules: `grafana/provisioning/alerting/otel-poc-alert-rules.yaml`
- contact points: `grafana/provisioning/alerting/otel-poc-contact-points.yaml`
- notification policies: `grafana/provisioning/alerting/otel-poc-notification-policies.yaml`

## Alert Webhook Mock

**Localização:** `tools/alert-webhook-mock`

**Tecnologia:** Python `http.server`

**Endpoint interno:** `http://alert-webhook-mock:8080`

### Endpoints

- `GET /health` para readiness básico
- `GET /requests` para histórico dos últimos requests recebidos
- `POST *` aceita o payload, persiste em memória circular e responde `200`

**Uso:** receiver local do contact point do Grafana para validar alertas provisionados sem depender de um webhook externo real.

## Geração de Carga

**Localização:** `tools/load-generator/generate-orders.ps1`

**Integração externa efetiva:** chamadas HTTP ao OrderService a partir do host.

**Uso:** gerar volume reproduzível para demonstrar dashboard, métricas de latência e alertas.

## Dependências Operacionais do Compose

- `zookeeper` sustenta o broker Kafka.
- `kafka` e `postgres` possuem health checks antes da subida dos serviços dependentes.
- `otelcol` depende de `lgtm` iniciado.
- `order-service`, `processing-worker` e `notification-worker` dependem do collector e da infraestrutura correspondente.
