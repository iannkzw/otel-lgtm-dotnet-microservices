# Transaction Outbox — CDC + Debezium Outbox Event Router Specification

## Problem Statement

O `OrderService` publica o evento Kafka **antes** de commitar o status `"published"` no banco de dados. O `ProcessingWorker` pode consumir essa mensagem e chamar `GET /orders/{id}` enquanto o pedido ainda está com status `"pending_publish"`, gerando o span `error.type = "order_not_published"` e descartando a mensagem. O problema é uma race condition estrutural na sequência de operações do endpoint `POST /orders`.

## Goals

- [x] Garantir que a mensagem Kafka só seja produzida **depois** que o pedido estiver com status `"published"` commitado no banco
- [x] Manter o `POST /orders` síncrono e rápido (sem depender do Kafka no caminho da requisição)
- [x] Eliminar o span `order_not_published` causado pela race condition
- [x] Preservar a chamada HTTP `GET /orders/{id}` no `ProcessingWorker` para manter o trace distribuído completo
- [x] Propagar o W3C `traceparent` do `POST /orders` ao `ProcessingWorker` via headers Kafka injetados pelo Debezium

## Out of Scope

- Polling Publisher (variante anterior, descartada por manter race condition possível)
- Retry com backoff exponencial para mensagens com falha
- Limpeza automática de registros da tabela `outbox_messages`
- KSQL / Kafka Streams para transformação de eventos CDC
- Alteração do contrato do evento Kafka (`OrderCreatedEvent`) ou da lógica do `ProcessingWorker`

---

## User Stories

### P1: Pedido e mensagem outbox salvos atomicamente ⭐ MVP

**User Story**: Como desenvolvedor, quero que o `POST /orders` salve o pedido (`published`) e a mensagem de outbox na mesma transação de banco de dados, para que nunca haja pedido criado sem mensagem de outbox correspondente — e o estado já seja consistente quando o CDC disparar.

**Why P1**: É a fundação do padrão. Com o pedido já em `published` na mesma TX da outbox, assim que o Debezium ler o WAL o banco já estará em estado correto para qualquer consumidor.

**Acceptance Criteria**:

1. WHEN `POST /orders` é chamado com `description` válido THEN o sistema SHALL salvar `Order` (status=`published`, `published_at_utc` preenchido) e `OutboxMessage` na mesma transação do Postgres
2. WHEN a transação de inserção falha THEN o sistema SHALL fazer rollback de ambas as entidades e retornar `500`
3. WHEN `POST /orders` é chamado THEN o sistema SHALL retornar `201 Created` sem nenhuma interação com o Kafka
4. WHEN `POST /orders` é chamado THEN `outbox_messages.idempotency_key` SHALL ser igual ao `order_id` (constraint UNIQUE — proteção contra duplicatas)

**Independent Test**: Chamar `POST /orders`; verificar no banco que `orders.status = 'published'` e `outbox_messages` tem linha correspondente. O Kafka NÃO deve ter recebido mensagem ainda (Debezium tem latência de poll de ~500ms).

---

### P1: Debezium captura outbox e publica no Kafka após commit ⭐ MVP

**User Story**: Como desenvolvedor, quero que o Debezium leia o WAL do Postgres e publique a mensagem no topic `orders` somente após o commit da transação, para que a race condition seja **impossível por design**.

**Why P1**: O CDC lê eventos do WAL apenas após o commit — é uma garantia estrutural do Postgres, não depende de lógica de aplicação.

**Acceptance Criteria**:

1. WHEN uma linha é inserida em `outbox_messages` e o TX commita THEN o Debezium SHALL publicar o `payload` no Kafka topic `orders` com `order_id` como chave da mensagem
2. WHEN o `ProcessingWorker` consome a mensagem e chama `GET /orders/{id}` THEN o sistema SHALL retornar `status = "published"` (race condition eliminada por design — o CDC só dispara após commit)
3. WHEN o Kafka Connect está indisponível THEN o Debezium SHALL parar de consumir WAL e reprocessar quando reconectado (mensagens não se perdem — ficam no WAL)

**Independent Test**: Chamar `POST /orders`; aguardar ~1s; verificar no Kafka UI que a mensagem chegou no topic `orders`; verificar que o `ProcessingWorker` processou sem gerar span `order_not_published`.

---

### P1: Trace context propagado via headers Kafka pelo Debezium ⭐ MVP

**User Story**: Como desenvolvedor, quero que o `traceparent` W3C do `POST /orders` seja armazenado na `outbox_messages` e propagado como header Kafka pelo Debezium, para que o trace distribuído seja contínuo de `POST /orders` até o `ProcessingWorker`.

**Why P1**: Sem propagação de trace, o span do `ProcessingWorker` apareceria como trace isolado no Grafana — perdendo a visibilidade end-to-end que é o objetivo central do PoC.

**Acceptance Criteria**:

1. WHEN `POST /orders` é executado THEN `outbox_messages.traceparent` SHALL ser preenchido com `Activity.Current?.Id` (W3C format: `00-{traceId}-{spanId}-{flags}`)
2. WHEN o Debezium publica a mensagem no Kafka THEN o header `traceparent` da mensagem Kafka SHALL conter o valor da coluna `outbox_messages.traceparent`
3. WHEN o `ProcessingWorker` consome a mensagem THEN `KafkaTracingHelper.Extract` SHALL recuperar o `traceparent` do header e linkar ao trace original do `POST /orders`
4. WHEN o header `traceparent` está ausente (mensagem legada) THEN o `ProcessingWorker` SHALL emitir `LogWarning` com `"Distributed context missing"` (comportamento atual preservado)

**Independent Test**: No Grafana → Traces, buscar o trace do `POST /orders`; o waterfall SHALL mostrar a cadeia: `POST /orders` → `kafka consume orders` (ProcessingWorker) → `GET /orders/{id}`.

---

### P2: Observabilidade do fluxo CDC no Grafana

**User Story**: Como desenvolvedor, quero ver o trace completo do pedido no Grafana incluindo os spans do CDC, para que o caminho de `POST /orders` até `notification-worker` seja visível em um único trace.

**Why P2**: O objetivo central do PoC é observabilidade. O trace end-to-end demonstra o valor do OTel em arquiteturas event-driven com CDC.

**Acceptance Criteria**:

1. WHEN um pedido é criado THEN o trace SHALL conter spans: `POST /orders` → `kafka consume orders` → `GET /orders/{id}` → `kafka publish notifications` → `kafka consume notifications`
2. WHEN o Debezium publica no Kafka THEN o `traceId` do span `kafka consume orders` SHALL ser **o mesmo** do span `POST /orders` (mesmo trace, span filho ou linked)
3. WHEN `outbox_messages.traceparent` está nulo (inserção sem contexto OTel ativo) THEN o Debezium SHALL publicar sem header `traceparent` e o worker SHALL emitir warning

---

## Edge Cases

- WHEN o processo do `OrderService` cai após COMMIT mas antes do Debezium ler o WAL THEN o Debezium SHALL reprocessar a partir da última posição do WAL ao reconectar → mensagem não se perde
- WHEN o mesmo `order_id` é inserido duas vezes na outbox THEN a constraint UNIQUE em `idempotency_key` SHALL rejeitar o segundo INSERT → rollback da TX inteira
- WHEN o Debezium publica no Kafka mas o Kafka retorna erro THEN o Debezium SHALL usar retry interno do Kafka Connect sem perder a mensagem (at-least-once delivery) → o `ProcessingWorker` deve ser idempotente
- WHEN o Kafka Connect está temporariamente indisponível THEN o Debezium SHALL retomar a partir da posição salva nos tópicos de offset (`connect_offsets`) ao reconectar

---

## Success Criteria

- [ ] `POST /orders` retorna `201 Created` sem nenhuma latência do Kafka
- [ ] Span `order_not_published` não aparece mais no Grafana para pedidos normais
- [ ] Trace do Grafana mostra cadeia completa: `POST /orders` → `kafka consume orders` → `GET /orders/{id}`
- [ ] `orders.status = 'published'` está commitado **antes** de qualquer consumo Kafka da mensagem (garantia estrutural do CDC)
- [ ] Build Docker passa sem erros (`docker run ... dotnet build otel-poc.sln`)
- [ ] `docker compose up` sobe kafka-connect + Debezium e o conector é registrado automaticamente

**User Story**: Como desenvolvedor, quero que o `POST /orders` salve o pedido e a mensagem de outbox na mesma transação de banco de dados, para que nunca haja um pedido criado sem uma mensagem de outbox correspondente.

**Why P1**: É a fundação do padrão. Sem atomicidade entre pedido e outbox, perder-se-iam pedidos sem evento Kafka.

**Acceptance Criteria**:

1. WHEN `POST /orders` é chamado com `description` válido THEN o sistema SHALL salvar `Order` (status=`pending_publish`) e `OutboxMessage` (status=`pending`) na mesma transação do Postgres
2. WHEN a transação de inserção falha THEN o sistema SHALL fazer rollback de ambas as entidades e retornar `500`
3. WHEN `POST /orders` é chamado THEN o sistema SHALL retornar `201 Created` sem interagir com o Kafka
4. WHEN `POST /orders` é chamado THEN o campo `idempotency_key` da `OutboxMessage` SHALL ser igual ao `order_id` (evita duplicatas por retries)

**Independent Test**: Chamar `POST /orders`, verificar no banco que `orders` tem status `pending_publish` e `outbox_messages` tem status `pending` com mesmo `order_id`. O Kafka NÃO deve ter recebido mensagem.

---

### P1: Relay worker publica mensagem após persistência ⭐ MVP

**User Story**: Como desenvolvedor, quero que o `OutboxRelayWorker` publique a mensagem Kafka e atualize o status do pedido para `"published"` atomicamente, para que o `ProcessingWorker` nunca veja um pedido em estado intermediário.

**Why P1**: É o núcleo da correção da race condition. O evento Kafka só chega ao consumidor quando o banco já está em estado consistente.

**Acceptance Criteria**:

1. WHEN o `OutboxRelayWorker` encontra uma `OutboxMessage` com status `pending` THEN SHALL publicar o payload no Kafka topic `orders`
2. WHEN a publicação Kafka for bem-sucedida THEN o sistema SHALL atualizar `outbox_messages.status = 'published'` E `orders.status = 'published'` E `orders.published_at_utc = now()` na **mesma transação**
3. WHEN o `ProcessingWorker` chamar `GET /orders/{id}` após consumir a mensagem THEN o sistema SHALL retornar o pedido com `status = "published"` (race condition eliminada)
4. WHEN o relay é iniciado THEN SHALL processar mensagens em lotes configuráveis (padrão: 10)
5. WHEN não há mensagens pendentes THEN SHALL aguardar um intervalo configurável antes do próximo poll (padrão: 500ms)

**Independent Test**: Chamar `POST /orders`, aguardar o intervalo de poll; verificar que `orders.status = 'published'`, `outbox_messages.status = 'published'` e que o Kafka recebeu a mensagem. O `ProcessingWorker` deve processar sem gerar span `order_not_published`.

---

### P1: Tratamento de falha na publicação Kafka ⭐ MVP

**User Story**: Como desenvolvedor, quero que o relay marque a mensagem como `failed` quando a publicação Kafka falhar, para que o estado seja rastreável e observável via spans/logs.

**Why P1**: Sem tratamento explícito de falha, mensagens trancadas em `pending` para sempre causam silêncio operacional.

**Acceptance Criteria**:

1. WHEN a publicação Kafka falhar THEN o sistema SHALL marcar `outbox_messages.status = 'failed'` E registrar o erro em log
2. WHEN a publicação falhar THEN SHALL adicionar `exception` ao span OTel com `ActivityStatusCode.Error`
3. WHEN a publicação falhar THEN o sistema SHALL continuar processando as demais mensagens do batch (falha isolada)
4. WHEN `outbox_messages.status = 'failed'` THEN `orders.status` SHALL permanecer `pending_publish` (não marcar como published sem confirmação Kafka)

**Independent Test**: Derrubar o Kafka, chamar `POST /orders`, aguardar o relay; verificar que `outbox_messages.status = 'failed'` e que `orders.status` continua `pending_publish`. Subir o Kafka e verificar que o span de erro está no Grafana.

---

### P2: Observabilidade do relay via OTel

**User Story**: Como desenvolvedor, quero spans e métricas do `OutboxRelayWorker` no Grafana, para que seja possível monitorar latência de relay, taxa de sucesso e falhas.

**Why P2**: Sem observabilidade, não é possível saber se o relay está funcionando ou com que latência as mensagens chegam ao Kafka.

**Acceptance Criteria**:

1. WHEN uma mensagem é processada pelo relay THEN SHALL existir um span `outbox relay` com tags `outbox.batch_size`, `outbox.order_id`, `messaging.destination.name`
2. WHEN uma mensagem é publicada com sucesso THEN SHALL incrementar contador `outbox.relay.published.total`
3. WHEN uma mensagem falha THEN SHALL incrementar contador `outbox.relay.failed.total`
4. WHEN o relay processa um batch THEN SHALL registrar histograma `outbox.relay.duration` em ms

**Independent Test**: Gerar pedidos via load generator, verificar no Grafana (Explore → Traces) spans `outbox relay` com o trace distribuído completo: `POST /orders` → `outbox relay` → `kafka publish orders` → `kafka consume orders` (ProcessingWorker).

---

### P3: Configuração de poll interval e batch size via appsettings

**User Story**: Como operador, quero configurar o intervalo de poll e o tamanho do batch do relay via variáveis de ambiente, sem recompilar.

**Why P3**: Permite ajuste operacional sem rebuild de imagem Docker.

**Acceptance Criteria**:

1. WHEN `OUTBOX_POLL_INTERVAL_MS` está definido THEN o relay SHALL usar esse valor como intervalo entre polls
2. WHEN `OUTBOX_BATCH_SIZE` está definido THEN o relay SHALL buscar no máximo esse número de mensagens por ciclo
3. WHEN as variáveis não estão definidas THEN o relay SHALL usar defaults: `500ms` e `10 mensagens`

---

## Edge Cases

- WHEN o processo do `OrderService` cai após INSERT da outbox mas antes do poll do relay THEN SHALL reprocessar no próximo startup (mensagem fica em `pending`)
- WHEN o mesmo `order_id` é inserido duas vezes na outbox THEN SHALL falhar com constraint `UNIQUE` em `idempotency_key` (proteção contra duplicata)
- WHEN o relay publica no Kafka mas falha ao commitar a transação de `SaveChangesAsync` THEN a mensagem fica em `pending` e será republicada — o consumidor deve ser idempotente
- WHEN não há pedidos pendentes por longo período THEN o relay SHALL fazer polls sem efeito colateral, consumindo CPU mínima

---

## Success Criteria

- [ ] `POST /orders` retorna `201 Created` sem latência adicional do Kafka
- [ ] Span `order_not_published` não aparece mais no Grafana para pedidos normais
- [ ] Span `outbox relay` aparece no trace distribuído ligado ao trace de `POST /orders`
- [ ] `orders.status = 'published'` sempre existirá **antes** de qualquer consumo Kafka da mensagem
- [ ] Build Docker passa sem erros (`docker run ... dotnet build otel-poc.sln`)
