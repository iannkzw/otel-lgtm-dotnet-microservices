# NotificationWorker Consumer + Persistência — Design

**Spec**: `.specs/features/notification-worker-consumer-persistencia/spec.md`
**Status**: Designed

---

## Architecture Overview

Esta feature expande apenas o `NotificationWorker`, preservando a baseline
validada de M1 e reutilizando os contratos ja estabilizados nas features
`order-service-api-persistencia` e `processing-worker-consumer-http-call`.

O worker deixara de emitir apenas heartbeats e passara a executar quatro
responsabilidades dentro do ultimo hop do trace distribuido iniciado em
`POST /orders`:

1. consumir mensagens do topic Kafka `notifications`;
2. reconstruir o contexto distribuido a partir de `traceparent` e
   `tracestate`;
3. criar um span manual `kafka consume notifications` com
   `ActivityKind.Consumer` e validar o payload minimo recebido;
4. persistir no PostgreSQL um resultado minimo do processamento, em tabela
   propria do servico, mantendo correlacao por `TraceId` sem alterar o payload
   Kafka.

Fluxo esperado por mensagem:

1. O consumer Kafka recebe uma mensagem do topic `notifications`.
2. O worker tenta desserializar `NotificationRequestedEvent`.
3. O worker extrai o contexto W3C de `traceparent` e `tracestate` quando os
   headers estiverem presentes e validos.
4. Um span manual `kafka consume notifications` e aberto com
   `ActivityKind.Consumer`, usando o contexto extraido quando disponivel ou um
   novo trace quando os headers estiverem ausentes ou invalidos.
5. Dentro desse span, o worker valida consistencia minima do payload.
6. Se o payload for valido, o worker monta o modelo interno de persistencia com
   os seis campos do evento mais `persistedAtUtc` e `traceId`.
7. O `NotificationDbContext` grava a linha em tabela propria do servico no
   PostgreSQL compartilhado da PoC.
8. Em caso de `consume_failed`, `invalid_payload` ou `persistence_failed`, o
   worker registra log estruturado, marca o span atual com erro observavel e
   segue saudavel para a proxima iteracao, sem retry, DLQ ou outbox.

O heartbeat atual pode permanecer apenas como mecanismo auxiliar de visibilidade
do processo host ate a implementacao ser validada, mas o caminho real de M2
passa a ser Kafka -> PostgreSQL.

---

## Design Decisions

### Reutilizar a estrategia W3C ja validada no ProcessingWorker

**Decision**: O `NotificationWorker` deve repetir a mesma abordagem de
extracao W3C ja validada em `ProcessingWorker.Messaging.KafkaTracingHelper`,
mantendo o contrato conceitual `Extract(Headers?)` e, se necessario futuramente,
compatibilidade com `Inject(Activity?, Headers)`.

**Reason**: O hop anterior ja comprovou em M2 a propagacao manual de
`traceparent` e `tracestate` entre `orders` e `notifications`. Repetir a mesma
estrategia reduz risco e preserva consistencia entre os hops Kafka da PoC.

**Trade-off**: O helper pode continuar duplicado localmente no
`NotificationWorker` nesta etapa. A extracao para biblioteca compartilhada segue
adiada para evitar refatoracao estrutural fora do escopo.

### Persistencia minima em tabela propria com `traceId` como correlacao de observabilidade

**Decision**: O `NotificationWorker` deve persistir um modelo interno minimo em
uma tabela propria, distinta da tabela `orders`, contendo os seis campos do
evento mais `persistedAtUtc` e `traceId`.

**Reason**: O milestone precisa de um artefato material do ultimo hop sem mudar
o payload externo de `notifications` nem misturar responsabilidades com a base
de dados do `OrderService`.

**Trade-off**: O modelo persistido fica intencionalmente orientado a
observabilidade e demonstracao, nao a um dominio final de notificacoes.

### `traceId` vem sempre do contexto corrente do span de consumo

**Decision**: O `traceId` persistido deve ser obtido do `Activity.Current` do
span `kafka consume notifications` ja aberto, refletindo o trace extraido dos
headers W3C quando valido ou o novo trace iniciado localmente quando houver
quebra de correlacao.

**Reason**: Isso preserva um mecanismo unico de correlacao com o Tempo sem
adicionar metadados ao payload Kafka.

**Trade-off**: Quando os headers vierem ausentes ou corrompidos, o registro
persistido continuara correlacionavel, mas a correlacao sera com um novo trace,
e nao com o trace original vindo do `OrderService`.

### Classificar erros por fase sem persistir estados intermediarios

**Decision**: A feature deve classificar `consume_failed`, `invalid_payload` e
`persistence_failed` por logs e spans, mas persistir no banco apenas o resultado
minimo de mensagens validas salvas com sucesso.

**Reason**: O objetivo desta etapa e mostrar causalidade clara do ultimo hop sem
expandir o escopo com tabela de falhas, replay ou controle de compensacao.

**Trade-off**: Falhas nao deixam artefato persistido no banco; a evidencia fica
restrita a trace e log, o que e aceitavel para esta PoC.

---

## Existing Components to Reuse

| Component | Location | How to Reuse |
|-----------|----------|--------------|
| Contrato consumido `NotificationRequestedEvent` | `src/ProcessingWorker/Contracts/NotificationRequestedEvent.cs` | Replicar ou referenciar o mesmo shape para desserializacao do payload vindo de `notifications` |
| Estrategia W3C de extracao Kafka | `src/ProcessingWorker/Messaging/KafkaTracingHelper.cs` | Reaproveitar a mesma logica de `Extract(Headers?)` para reconstruir o contexto pai |
| Bootstrap OTel do worker | `src/NotificationWorker/Extensions/OtelExtensions.cs` | Manter export OTLP atual e adicionar `ActivitySource` manual do consumo; spans de DB virao da instrumentacao EF Core |
| Host e loop atuais | `src/NotificationWorker/Program.cs`, `src/NotificationWorker/Worker.cs` | Substituir o heartbeat puro por loop com consumer Kafka e persistencia, mantendo o host simples |
| Baseline PostgreSQL da PoC | `docker-compose.yaml` e infra existente | Reusar a mesma connection string compartilhada, com tabela propria do `NotificationWorker` |

---

## Components

### Kafka Consumer Worker

- **Purpose**: Ler continuamente mensagens do topic `notifications`, abrir o
  span de consumo, classificar falhas e acionar o pipeline de validacao e
  persistencia.
- **Location**: `src/NotificationWorker/Worker.cs`
- **Responsibilities**:
  - criar/configurar `IConsumer<string, string>` com group id dedicado;
  - assinar o topic `notifications`;
  - capturar falhas de `Consume` como `consume_failed` sem derrubar o host;
  - coordenar desserializacao, validacao e persistencia por mensagem.

### Notification Activity Source

- **Purpose**: Representar o span manual de consumo/processamento do ultimo hop
  Kafka.
- **Location**: `src/NotificationWorker/Extensions/OtelExtensions.cs`
- **Span names**:
  - `kafka consume notifications`

### Kafka Tracing Helper Adaptado

- **Purpose**: Extrair o contexto W3C dos headers do evento consumido.
- **Location**: `src/NotificationWorker/Messaging/KafkaTracingHelper.cs`
- **Interfaces**:
  - `ActivityContext? Extract(Headers? headers)`

### Notification Payload Validator

- **Purpose**: Centralizar as validacoes semanticas minimas do payload antes de
  tocar no banco.
- **Location**: `src/NotificationWorker/Contracts/` ou pasta equivalente de
  processamento interno.
- **Validation rules**:
  - `orderId` obrigatorio e valido;
  - `description` obrigatoria e nao vazia;
  - `status` obrigatorio;
  - `publishedAtUtc` obrigatorio quando `status = published`;
  - `processedAtUtc` nao pode ser anterior a `createdAtUtc`.

### NotificationDbContext

- **Purpose**: Persistir e consultar o resultado minimo processado pelo worker.
- **Location**: `src/NotificationWorker/Data/NotificationDbContext.cs`
- **Entities**: `PersistedNotification`
- **Dependencies**: `Npgsql.EntityFrameworkCore.PostgreSQL`

### PersistedNotification Entity

- **Purpose**: Representar a linha persistida pelo `NotificationWorker` para
  demonstrar o fechamento do fluxo de M2.
- **Location**: `src/NotificationWorker/Data/PersistedNotification.cs`
- **Fields**:
  - `Id: Guid`
  - `OrderId: Guid`
  - `Description: string`
  - `Status: string`
  - `CreatedAtUtc: DateTimeOffset`
  - `PublishedAtUtc: DateTimeOffset`
  - `ProcessedAtUtc: DateTimeOffset`
  - `PersistedAtUtc: DateTimeOffset`
  - `TraceId: string`

---

## Data Model

### PostgreSQL Table: notification_results

| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` | Primary key tecnico do registro persistido |
| `order_id` | `uuid` | Identificador vindo do evento `notifications` |
| `description` | `text` | Copia literal do payload consumido |
| `status` | `text` | Copia literal do payload consumido |
| `created_at_utc` | `timestamp with time zone` | Copia literal do payload consumido |
| `published_at_utc` | `timestamp with time zone` | Copia literal do payload consumido |
| `processed_at_utc` | `timestamp with time zone` | Copia literal do payload consumido |
| `persisted_at_utc` | `timestamp with time zone` | Preenchido no momento da gravacao pelo worker |
| `trace_id` | `text` | `TraceId` do span corrente para correlacao com o Tempo |

Indices recomendados para a PoC:

- indice por `order_id` para busca direta por pedido;
- indice por `trace_id` para correlacao rapida com o Tempo;
- nenhuma constraint de unicidade adicional alem da chave primaria nesta etapa,
  para nao antecipar regras de idempotencia fora do escopo.

### Consumed message: topic `notifications`

Payload esperado, coerente com a feature anterior:

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

Observacoes:

- o payload Kafka permanece intacto;
- `persistedAtUtc` e `traceId` existem apenas no modelo interno e no banco;
- a persistencia copia os campos do evento sem reformatar o contrato externo.

---

## Processing Flow

### Happy path

1. Consumir mensagem de `notifications`.
2. Extrair `ActivityContext` dos headers Kafka.
3. Abrir span `kafka consume notifications` com `ActivityKind.Consumer`.
4. Logar inicio do processamento com `orderId`, topic, `TraceId` e `SpanId`.
5. Desserializar `NotificationRequestedEvent`.
6. Validar consistencia minima do payload.
7. Montar `PersistedNotification` com `persistedAtUtc = UtcNow` e
   `traceId = Activity.Current.TraceId`.
8. Salvar a entidade via `NotificationDbContext`.
9. Logar sucesso da persistencia com `orderId`, `TraceId`, `SpanId` e
   timestamp gravado.

### Error path: consume_failed

1. `Consume` falha por indisponibilidade do broker, erro de infraestrutura ou
   excecao tecnica fora do shutdown esperado.
2. O worker registra erro estruturado classificado como `consume_failed`, com
   metadados Kafka disponiveis.
3. Como ainda nao existe payload confiavel, nenhuma validacao ou persistencia e
   tentada.
4. O loop segue vivo para a proxima iteracao e volta a consumir quando a
   infraestrutura responder novamente.

### Error path: invalid_payload

1. A mensagem foi consumida, mas o JSON e malformado ou semanticamente invalido.
2. O span `kafka consume notifications` e marcado com erro observavel.
3. O worker registra erro estruturado classificado como `invalid_payload`, com
   o motivo principal e o que houver de metadado Kafka disponivel.
4. Nenhuma linha e persistida no PostgreSQL.
5. O loop segue saudavel para a proxima mensagem.

### Error path: persistence_failed

1. O payload e valido e o span de consumo esta ativo.
2. O worker monta a entidade e tenta salvar no PostgreSQL.
3. O span de banco gerado por EF Core/Npgsql registra a excecao.
4. O span `kafka consume notifications` tambem e marcado com erro.
5. O worker registra erro estruturado classificado como `persistence_failed`,
   com `orderId`, `TraceId`, `SpanId` e tipo da excecao.
6. O host continua apto a processar novas mensagens.

---

## Configuration

### Required Configuration

| Key | Source | Purpose |
|-----|--------|---------|
| `KAFKA_BOOTSTRAP_SERVERS` | compose atual | consumer Kafka |
| `POSTGRES_CONNECTION_STRING` | compose atual | conexao do `NotificationDbContext` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | compose atual | exportacao OTLP |
| `OTEL_SERVICE_NAME` | compose atual | `service.name` do worker |

### Optional Defaults

| Key | Default | Purpose |
|-----|---------|---------|
| `KAFKA_TOPIC_NOTIFICATIONS` | `notifications` | topic consumido |
| `KAFKA_GROUP_ID_NOTIFICATION` | `notification-worker` | group id do consumer |

---

## Observability Plan

### Traces

- `POST /orders` continua como span root automatico no `order-service`.
- `kafka publish orders` continua como span manual do `OrderService`.
- `kafka consume orders`, `GET /orders/{id}` e `kafka publish notifications`
  continuam no `processing-worker` como ja validado.
- `kafka consume notifications` sera o span manual do `notification-worker`
  dentro do mesmo `TraceId` quando os headers W3C forem validos.
- o span de banco surgira como filho do span de consumo por meio da
  instrumentacao EF Core/Npgsql.

Tags recomendadas no span manual de consumo:

- `messaging.system = kafka`
- `messaging.destination.name = notifications`
- `messaging.operation = receive`
- `messaging.kafka.message.key = {orderId}` quando disponivel
- `order.id = {orderId}` quando o payload for valido
- `error.type = consume_failed|invalid_payload|persistence_failed` quando
  aplicavel

### Logs

Logs do worker devem sempre buscar correlacao direta com o trace:

- inicio do processamento com topic, `orderId`, `TraceId` e `SpanId`;
- warning para headers W3C ausentes ou invalidos, indicando quebra de
  correlacao sem parar o fluxo;
- sucesso da persistencia com `orderId`, `TraceId` e `persistedAtUtc`;
- erro classificado em `consume_failed`, `invalid_payload` ou
  `persistence_failed` com contexto suficiente para troubleshooting.

### Tempo Validation Plan

No caminho feliz, o Tempo deve mostrar um unico trace com:

1. `POST /orders`
2. spans de banco do `OrderService`
3. `kafka publish orders`
4. `kafka consume orders`
5. `GET /orders/{id}`
6. `kafka publish notifications`
7. `kafka consume notifications`
8. span de banco do `notification-worker`

Nos caminhos de erro:

- `invalid_payload`: deve haver span `kafka consume notifications` com erro,
  sem span DB bem-sucedido subsequente;
- `persistence_failed`: deve haver span de consumo e span DB com erro no mesmo
  hop;
- `consume_failed`: o log deve evidenciar a falha de consumo e o container deve
  continuar em `Up`, ainda que nem sempre exista span utilizavel quando o erro
  ocorrer antes da abertura do contexto de processamento.

---

## Local Validation Plan

### Happy path

1. Subir o ambiente com `docker compose up -d --build`.
2. Criar um pedido real com `POST /orders`.
3. Confirmar que o `ProcessingWorker` publicou em `notifications`.
4. Confirmar nos logs do `notification-worker` o consumo e a persistencia com o
   mesmo `orderId`.
5. Consultar diretamente a tabela `notification_results` no PostgreSQL e
   validar os campos minimos persistidos.

### Error path: invalid_payload

1. Publicar manualmente uma mensagem invalida em `notifications`.
2. Confirmar log classificado como `invalid_payload`.
3. Confirmar ausencia de nova linha na tabela `notification_results`.
4. Confirmar que o `notification-worker` continua em execucao.

### Error path: persistence_failed

1. Tornar o PostgreSQL indisponivel ou induzir falha controlada de conexao.
2. Produzir uma mensagem valida em `notifications`.
3. Confirmar log classificado como `persistence_failed`.
4. Confirmar que o worker continua saudavel apos a excecao.

### Error path: consume_failed

1. Induzir falha transitoria do Kafka.
2. Confirmar log classificado como `consume_failed`.
3. Confirmar que o loop volta a consumir quando o broker retorna.

---

## Implementation Notes

- `Program.cs` deve continuar enxuto, registrando `DbContext`, consumer Kafka e
  o hosted service sem mover logica pesada para setup inline.
- O bootstrap OTel do `NotificationWorker` precisa adicionar
  `AddEntityFrameworkCoreInstrumentation()` e `AddSource(...)` para o span
  manual de consumo.
- O loop do worker deve respeitar `CancellationToken` em `Consume`,
  desserializacao e persistencia.
- O helper W3C deve ficar isolado da logica de negocio para facilitar futura
  extracao compartilhada.
- Retry, backoff, DLQ, outbox e idempotencia forte continuam explicitamente fora
  de escopo desta feature.