# Code Conventions

## Nomenclatura

### Arquivos e Diretórios

- Serviços usam nomes em PascalCase: `OrderService`, `ProcessingWorker`, `NotificationWorker`.
- Diretórios internos seguem responsabilidade técnica: `Contracts`, `Data`, `Extensions`, `Messaging`, `Metrics`, `Clients`.
- Arquivos de configuração e infraestrutura usam kebab-case ou nomes consagrados do ecossistema: `docker-compose.yaml`, `otelcol.yaml`, `drop-health-checks.yaml`.

### Namespaces e Tipos

- Namespaces seguem o padrão `<Servico>.<Area>`.
- Classes públicas usam PascalCase: `KafkaOrderPublisher`, `OrderDbContext`, `NotificationLagRefresher`.
- Interfaces usam prefixo `I`: `IKafkaOrderPublisher`, `IOrderServiceClient`, `INotificationMetrics`.
- DTOs e eventos usam `sealed record` quando o tipo é imutável de transporte: `CreateOrderRequest`, `OrderCreatedEvent`, `NotificationRequestedEvent`.

### Campos, Constantes e Resultados

- Campos privados usam `_camelCase`: `_topic`, `_consumerGroupId`, `_ordersCreatedCounter`.
- Constantes de status e resultados usam PascalCase com valor string em snake/kebab semântico: `OrderCreateResults.PublishFailed = "publish_failed"`.
- Chaves de configuração usam `UPPER_SNAKE_CASE`: `KAFKA_BOOTSTRAP_SERVERS`, `POSTGRES_CONNECTION_STRING`, `OTEL_EXPORTER_OTLP_ENDPOINT`.

## Organização Interna dos Arquivos

- `Program.cs` concentra bootstrap e wiring do serviço.
- Regras de negócio operacionais ficam em `Worker.cs`, publishers, clients e serviços auxiliares.
- `DbContext` e entidades EF Core ficam separados em `Data/`.
- Instrumentação e registro de métricas ficam isolados de `Program.cs` em extensões e classes específicas.

## Estilo de Código C#

- Usa file-scoped namespace quando o arquivo não é top-level.
- Usa primary constructors em classes como `Worker(...)` e `OrderServiceClient(HttpClient httpClient)`.
- Usa `var` quando o tipo é óbvio pelo construtor ou chamada.
- Usa coleções literais modernas quando o compilador suporta: `[]` para arrays/listas curtas de tags.
- Mantém métodos pequenos para cada responsabilidade, com helpers privados quando a lógica cresce, como `HandleLookupOutcome`.

## Padrões de Instrumentação

- Cada serviço define um `ActivitySourceName` central em `OtelExtensions.cs`.
- Spans manuais para Kafka usam nomes verbais e diretos: `kafka publish orders`, `kafka consume notifications`.
- Tags seguem semântica OpenTelemetry sempre que possível:
	- `messaging.system`
	- `messaging.destination.name`
	- `messaging.operation`
	- `messaging.kafka.message.key`
	- `order.id`

## Padrões de Métricas

- Cada serviço define um `MeterName` próprio.
- Nomes de métricas usam hierarquia com pontos:
	- `orders.created.total`
	- `orders.create.duration`
	- `orders.backlog.current`
	- `orders.processed.total`
	- `orders.processing.duration`
	- `notifications.persisted.total`
	- `notifications.persistence.duration`
	- `kafka.consumer.lag`
- Resultados são sempre enviados como tag `result`.
- Métricas observáveis de backlog e lag usam snapshots thread-safe dedicados.

## Padrões de Persistência

- O mapeamento EF Core é explícito e orientado a tabela/coluna real.
- Colunas usam snake_case no PostgreSQL: `created_at_utc`, `published_at_utc`, `trace_id`.
- A convenção é descrever o schema no `OnModelCreating`, mesmo para entidades simples.
- Quando `EnsureCreatedAsync()` não é suficiente, o bootstrap complementa com DDL explícito no startup, como em `NotificationWorker`.

## Padrões de Mensageria

- Kafka usa chave da mensagem baseada no `OrderId` serializado como string.
- Payloads são JSON.
- O OrderService usa `JsonSerializer.Serialize(orderEvent)` com casing padrão do record.
- O ProcessingWorker usa `JsonNamingPolicy.CamelCase` ao publicar notificações.
- O contexto W3C é propagado por headers Kafka com helper dedicado em cada serviço.

## Tratamento de Erros e Logs

- Falhas relevantes sempre registram `TraceId` e `SpanId` quando disponíveis.
- Logs usam mensagens estruturadas com placeholders nomeados.
- O código prefere classificar falhas por resultado em vez de apenas registrar exceção genérica.
- Loops de consumo Kafka continuam vivos após falhas recuperáveis.
- `ActivityStatusCode.Error` é definido quando a falha precisa aparecer no trace.

## Comentários e Documentação Inline

- O código tem poucos comentários inline; a preferência é por nomes claros e separação de responsabilidades.
- Comentários aparecem mais em scripts e config operacional do que nas classes C#.
- A documentação do comportamento operacional está concentrada no `README.md` e nos artefatos de `.specs`.

## Divergências Observadas

- Há uma diferença de versão do `Confluent.Kafka`: `2.11.0` no OrderService e `2.5.0` nos workers.
- Há dependências preview/beta de EF Core instrumentation e Npgsql, coerentes com o alvo `net10.0` atual da PoC.
