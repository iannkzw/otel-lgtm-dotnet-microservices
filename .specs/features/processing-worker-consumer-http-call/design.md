# ProcessingWorker Consumer + HTTP Call — Design

**Spec**: `.specs/features/processing-worker-consumer-http-call/spec.md`
**Status**: Designed

---

## Architecture Overview

Esta feature expande apenas o `ProcessingWorker`, preservando a baseline validada de M1 e consumindo os contratos ja consolidados na feature `order-service-api-persistencia`.

O worker deixara de emitir apenas heartbeats e passara a executar quatro responsabilidades dentro do mesmo fluxo distribuido iniciado pelo `POST /orders`:

1. consumir mensagens do topic Kafka `orders`;
2. reconstruir o contexto distribuido a partir de `traceparent` e `tracestate`;
3. consultar `GET /orders/{id}` no `OrderService` com `HttpClient` instrumentado;
4. publicar um evento enriquecido no topic Kafka `notifications`, preservando o mesmo `TraceId`.

Fluxo esperado por mensagem:

1. O consumer Kafka recebe uma mensagem do topic `orders`.
2. O worker desserializa `OrderCreatedEvent` e extrai o contexto W3C dos headers.
3. Um span manual `kafka consume orders` e aberto com `ActivityKind.Consumer`, usando o contexto extraido quando disponivel.
4. Dentro desse span, o worker chama `GET /orders/{id}` via `HttpClient` instrumentado.
5. Se a resposta HTTP for valida, o worker monta a mensagem minima de `notifications`.
6. Um span manual `kafka publish notifications` representa a producao do proximo evento.
7. O producer injeta `traceparent` e `tracestate` nos headers Kafka do novo evento.
8. Em caso de `404`, `5xx`, timeout, falha de rede, payload invalido ou publish Kafka com erro, o worker nao publica a proxima mensagem, registra log estruturado e segue saudavel.

O heartbeat atual deve permanecer apenas como mecanismo auxiliar de visibilidade do processo host. O caminho real de M2 passa a ser validado pelo fluxo Kafka -> HTTP -> Kafka.

---

## Design Decisions

### Reutilizar o modelo do `KafkaTracingHelper` ja validado no OrderService

**Decision**: O `ProcessingWorker` deve reutilizar a mesma abordagem de injecao/extracao W3C ja implementada em `OrderService.Messaging.KafkaTracingHelper`, mantendo a mesma assinatura conceitual de `Inject(Activity?, Headers)` e `Extract(Headers)`.

**Reason**: A feature anterior ja validou localmente a propagacao manual de `traceparent` no topic `orders`. Repetir o mesmo contrato reduz risco e evita duas estrategias de propagacao no mesmo milestone.

**Trade-off**: Nesta iteracao, o helper pode ser duplicado ou adaptado localmente no `ProcessingWorker` se a extracao compartilhada ainda nao estiver em um projeto comum. A extracao para biblioteca compartilhada continua adiada para evitar churn estrutural fora do escopo.

### Evento `orders` como gatilho; HTTP como fonte de verdade

**Decision**: O payload Kafka consumido continuara minimo e sera usado apenas para localizar e correlacionar o pedido. O estado enriquecido sempre vira de `GET /orders/{id}`.

**Reason**: O `OrderService` ja persiste o pedido e expoe um contrato estavel via `OrderResponse`; isso evita inflar o evento `orders` antes da hora e mantem a feature coerente com AD-014.

**Trade-off**: O worker ganha dependencia sincrona do `OrderService`, aumentando latencia e superficie de falha no processamento.

### `HttpClient` nomeado com timeout explicito

**Decision**: O worker deve usar um `HttpClient` registrado em DI, com `BaseAddress` vindo de configuracao e timeout curto o bastante para tornar erros observaveis sem travar o loop por muito tempo.

**Reason**: A instrumentacao HTTP automatica ja existe no bootstrap OTel do worker, e um cliente nomeado simplifica configuracao, testes locais e controle de timeout.

**Trade-off**: Um timeout agressivo pode falhar mais cedo em ambientes instaveis, mas isso e desejavel para a PoC de observabilidade.

### Falhas de enriquecimento nao derrubam o host e nao produzem em `notifications`

**Decision**: Qualquer falha entre consumo e enriquecimento HTTP deve ser observavel no trace e nos logs, mas nao deve encerrar o `BackgroundService` nem produzir uma mensagem parcial em `notifications`.

**Reason**: A feature precisa demonstrar claramente onde a cadeia parou, sem antecipar retry, DLQ ou persistencia compensatoria.

**Trade-off**: A mensagem consumida pode ser perdida ou depender da estrategia de offset/commit adotada na implementacao. Esse risco e conhecido e permanece fora de escopo nesta fase.

---

## Existing Components to Reuse

| Component | Location | How to Reuse |
|-----------|----------|--------------|
| Contrato consumido `OrderCreatedEvent` | `src/OrderService/Contracts/OrderCreatedEvent.cs` | Replicar ou referenciar o mesmo shape para desserializacao do payload vindo de `orders` |
| Contrato HTTP `OrderResponse` | `src/OrderService/Contracts/OrderResponse.cs` | Usar como fonte de verdade do payload esperado em `GET /orders/{id}` |
| Helper W3C Kafka | `src/OrderService/Messaging/KafkaTracingHelper.cs` | Reaproveitar a mesma logica de `Inject`/`Extract` no worker |
| Bootstrap OTel do worker | `src/ProcessingWorker/Extensions/OtelExtensions.cs` | Manter `HttpClientInstrumentation` e adicionar `ActivitySource` manual para consumo e publish |
| Host e background loop atuais | `src/ProcessingWorker/Program.cs`, `src/ProcessingWorker/Worker.cs` | Substituir o heartbeat puro por loop com consumo Kafka e manter o host simples |

---

## Components

### Kafka Consumer Worker

- **Purpose**: Ler continuamente mensagens do topic `orders`, desserializar o payload e acionar o pipeline de processamento.
- **Location**: `src/ProcessingWorker/Worker.cs`
- **Responsibilities**:
  - criar/configurar `IConsumer<string, string>` com group id dedicado;
  - assinar o topic `orders`;
  - consumir mensagens com cancelamento cooperativo;
  - validar payload minimo antes do enriquecimento HTTP.

### Processing Activity Source

- **Purpose**: Representar spans manuais de consumo e publish Kafka.
- **Location**: `src/ProcessingWorker/Extensions/OtelExtensions.cs`
- **Span names**:
  - `kafka consume orders`
  - `kafka publish notifications`

### Kafka Tracing Helper Adaptado

- **Purpose**: Extrair o contexto pai do evento consumido e injetar o contexto atual no evento publicado.
- **Location**: `src/ProcessingWorker/Messaging/KafkaTracingHelper.cs` ou pasta equivalente de messaging.
- **Interfaces**:
  - `ActivityContext? Extract(Headers headers)`
  - `void Inject(Activity? activity, Headers headers)`

### Order Service Client

- **Purpose**: Encapsular a chamada `GET /orders/{id}` e isolar as regras de sucesso, `404` e falhas tecnicas.
- **Location**: `src/ProcessingWorker/Clients/` ou pasta equivalente.
- **Interfaces**:
  - `Task<OrderResponse?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken)`
- **Dependencies**: `HttpClient`, logger, serializacao JSON.

### Notification Publisher

- **Purpose**: Publicar a mensagem enriquecida no topic `notifications` sem expor detalhes Kafka no loop principal.
- **Location**: `src/ProcessingWorker/Messaging/`
- **Interfaces**:
  - `Task PublishAsync(NotificationRequestedEvent message, CancellationToken cancellationToken)`
- **Dependencies**: `Confluent.Kafka`, `KafkaTracingHelper`, logger.

---

## Message and HTTP Contracts

### Consumed message: topic `orders`

Payload esperado, coerente com a feature anterior:

```json
{
  "orderId": "58ab9539-9e92-4002-982b-e6d16fe178ca",
  "description": "demo order",
  "createdAtUtc": "2026-03-19T18:30:00.0000000+00:00"
}
```

Headers esperados:

- `traceparent`: obrigatorio para continuidade do trace quando presente e valido
- `tracestate`: opcional e preservado quando existir

### HTTP enrichment: `GET /orders/{id}`

Contrato esperado do `OrderService`:

```json
{
  "orderId": "58ab9539-9e92-4002-982b-e6d16fe178ca",
  "description": "demo order",
  "status": "published",
  "createdAtUtc": "2026-03-19T18:30:00.0000000+00:00",
  "publishedAtUtc": "2026-03-19T18:30:00.1500000+00:00"
}
```

Regras do cliente HTTP:

- `200 OK`: seguir para montagem do evento de `notifications`
- `404 Not Found`: tratar como erro observavel de negocio; nao publicar
- `5xx`: tratar como falha tecnica; nao publicar
- timeout: tratar como falha tecnica; nao publicar
- falha de rede/DNS/conexao: tratar como falha tecnica; nao publicar

Validacoes de consistencia apos `200 OK`:

- `orderId` da resposta deve coincidir com o `orderId` consumido
- `publishedAtUtc` deve estar preenchido quando `status = published`
- campos obrigatorios nao podem vir nulos ou vazios

### Produced message: topic `notifications`

Payload minimo da nova mensagem:

```json
{
  "orderId": "58ab9539-9e92-4002-982b-e6d16fe178ca",
  "description": "demo order",
  "status": "published",
  "createdAtUtc": "2026-03-19T18:30:00.0000000+00:00",
  "publishedAtUtc": "2026-03-19T18:30:00.1500000+00:00",
  "processedAtUtc": "2026-03-19T18:30:01.0000000+00:00"
}
```

Esse contrato permanece deliberadamente minimo para permitir que a proxima feature de `NotificationWorker` foque em consumo, correlacao e persistencia sem rediscutir o payload.

---

## Processing Flow

### Happy path

1. Consumir mensagem de `orders`.
2. Desserializar `OrderCreatedEvent`.
3. Extrair `ActivityContext` dos headers Kafka.
4. Abrir span `kafka consume orders` com `ActivityKind.Consumer`.
5. Logar inicio do processamento com `orderId`, `TraceId` e `SpanId`.
6. Chamar `GET /orders/{id}` usando `HttpClient` instrumentado.
7. Validar contrato HTTP retornado.
8. Montar payload `notifications` com `processedAtUtc = UtcNow`.
9. Abrir span `kafka publish notifications` como filho do span de consumo.
10. Injetar `traceparent` e `tracestate` nos headers da nova mensagem.
11. Publicar no topic `notifications`.
12. Logar sucesso com topic de origem, topic de destino e identificadores de correlacao.

### Error path: `404`

1. Span de consumo continua ativo.
2. `HttpClient` gera span de saida para `GET /orders/{id}` com `404`.
3. O worker registra erro de negocio, marca o span de consumo com status de erro observavel e encerra o processamento da mensagem.
4. Nenhum span de `kafka publish notifications` deve existir para essa mensagem.

### Error path: `5xx`, timeout ou falha de rede

1. Span de consumo continua ativo.
2. O span HTTP filho e marcado com erro ou excecao.
3. O worker registra excecao com `orderId`, `TraceId`, `SpanId` e tipo de falha.
4. Nenhuma mensagem e publicada em `notifications`.
5. O loop segue vivo para a proxima mensagem.

---

## Configuration

### Required Configuration

| Key | Source | Purpose |
|-----|--------|---------|
| `KAFKA_BOOTSTRAP_SERVERS` | compose atual | consumer e producer Kafka |
| `ORDER_SERVICE_BASE_URL` | novo no compose/appsettings | base address do `GET /orders/{id}` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | compose atual | exportacao OTLP |
| `OTEL_SERVICE_NAME` | compose atual | `service.name` do worker |

### Optional Defaults

| Key | Default | Purpose |
|-----|---------|---------|
| `KAFKA_TOPIC_ORDERS` | `orders` | topic consumido |
| `KAFKA_TOPIC_NOTIFICATIONS` | `notifications` | topic publicado |
| `KAFKA_GROUP_ID_PROCESSING` | `processing-worker` | group id do consumer |
| `ORDER_SERVICE_TIMEOUT_SECONDS` | `5` | timeout do `HttpClient` |

---

## Observability Plan

### Traces

- `POST /orders` continua sendo o span root no `order-service`.
- `kafka publish orders` permanece como span manual do producer atual.
- `kafka consume orders` sera o primeiro span manual do `processing-worker` no mesmo `TraceId`.
- `GET /orders/{id}` surgira como span automatico de `HttpClient` filho do span de consumo.
- `kafka publish notifications` sera um span manual filho do mesmo contexto no worker.

Tags recomendadas nos spans manuais:

- `messaging.system = kafka`
- `messaging.destination.name = orders|notifications`
- `messaging.operation = receive|publish`
- `messaging.kafka.message.key = {orderId}` quando aplicavel
- `order.id = {orderId}`
- `error.type` ou descricao equivalente nos cenarios de falha

### Logs

Logs do worker devem sempre buscar correlacao direta com o trace:

- inicio do processamento com `orderId`, topic e identificadores do span atual;
- sucesso do enriquecimento HTTP e da publicacao em `notifications`;
- `404`, `5xx`, timeout, falha de rede e erro de desserializacao com contexto suficiente para troubleshooting.

### Tempo Validation Plan

No caminho feliz, o Tempo deve mostrar um unico trace com:

1. `POST /orders`
2. spans de banco do `OrderService`
3. `kafka publish orders`
4. `kafka consume orders`
5. `GET /orders/{id}`
6. `kafka publish notifications`

Nos caminhos de erro:

- `404`: deve haver span HTTP com `404`, mas nao deve haver span de producer para `notifications`
- `5xx`/timeout/rede: deve haver span HTTP com falha observavel, sem span de producer subsequente

---

## Implementation Notes

- `Program.cs` deve continuar enxuto, registrando `HttpClient`, consumer/producer Kafka e o hosted service sem mover logica pesada para setup inline.
- O loop do worker precisa respeitar `CancellationToken` em consumo, chamadas HTTP e publish.
- O helper W3C deve permanecer isolado da logica de negocio para facilitar futura extracao compartilhada com `NotificationWorker`.
- Retry, backoff, DLQ e persistencia compensatoria continuam explicitamente fora de escopo desta feature.
- O heartbeat pode ser mantido com menor protagonismo ou removido quando o fluxo real de processamento estiver validado; a implementacao deve evitar spans redundantes que poluam a leitura do Tempo.