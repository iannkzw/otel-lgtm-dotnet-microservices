# ProcessingWorker Consumer + HTTP Call — Specification

**Milestone**: M2 — Fluxo de Eventos End-to-End
**Status**: Specified

---

## Problem Statement

O `OrderService` ja publica eventos no topic `orders` com propagacao manual de `traceparent` e `tracestate`, mas o `ProcessingWorker` ainda so emite heartbeats. Sem um consumer real, M2 ainda nao demonstra a continuidade do trace apos a publicacao Kafka nem o enriquecimento via HTTP previsto desde a conclusao da feature `order-service-api-persistencia`.

Esta feature precisa fazer o `ProcessingWorker` consumir a mensagem de `orders`, reconstruir o contexto distribuido a partir dos headers W3C, consultar `GET /orders/{id}` no `OrderService` e publicar a proxima mensagem no topic `notifications` preservando o mesmo trace. O resultado esperado e um caminho observavel no Tempo conectando Kafka consumer -> HTTP client -> Kafka producer dentro do mesmo `TraceId` iniciado pelo `POST /orders`.

## Goals

- [ ] Consumir mensagens do topic Kafka `orders` no `ProcessingWorker`
- [ ] Extrair `traceparent` e `tracestate` dos headers Kafka e reconstruir o contexto pai
- [ ] Criar span manual de consumo/processamento do evento com `ActivityKind.Consumer`
- [ ] Chamar `GET /orders/{id}` no `OrderService` para enriquecer o processamento
- [ ] Publicar mensagem no topic Kafka `notifications` com propagacao manual do contexto W3C preservada
- [ ] Registrar logs estruturados com `orderId`, `TraceId` e `SpanId` nos cenarios de sucesso e erro
- [ ] Definir criterios de validacao local e no Tempo para comprovar o trace distribuido parcial de M2

## Out of Scope

- Implementacao do `NotificationWorker`
- Persistencia de resultado do `ProcessingWorker`
- Retry, backoff, DLQ, transactional outbox ou garantias avancadas de entrega
- Alteracoes no contrato do `POST /orders` alem do que ja foi consolidado
- Metricas customizadas, dashboards e alertas de M3
- Mudanca para contrato Avro/Schema Registry

---

## Message Contracts

### Consumed message: topic `orders`

Origem: `OrderService.Contracts.OrderCreatedEvent`

**Expected payload**:

```json
{
  "orderId": "58ab9539-9e92-4002-982b-e6d16fe178ca",
  "description": "demo order",
  "createdAtUtc": "2026-03-19T18:30:00.0000000+00:00"
}
```

**Required headers**:

- `traceparent`: obrigatorio para reconstruir o contexto distribuido
- `tracestate`: opcional, mas deve ser preservado quando presente

### Enrichment response: `GET /orders/{id}`

Origem: `OrderService.Contracts.OrderResponse`

**Expected success payload**:

```json
{
  "orderId": "58ab9539-9e92-4002-982b-e6d16fe178ca",
  "description": "demo order",
  "status": "published",
  "createdAtUtc": "2026-03-19T18:30:00.0000000+00:00",
  "publishedAtUtc": "2026-03-19T18:30:00.1500000+00:00"
}
```

### Produced message: topic `notifications`

**Minimum payload**:

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

**Required headers**:

- `traceparent`: obrigatorio
- `tracestate`: opcional, mas deve ser propagado quando existir no contexto atual

O contrato de `notifications` deve permanecer minimo e suficiente para o `NotificationWorker` validar a correlacao e iniciar sua propria feature sem antecipar detalhes de persistencia final.

---

## User Stories

### P1: Consumir o evento `orders` e reconstruir o contexto distribuido ⭐ MVP

**User Story**: Como `ProcessingWorker`, quero consumir mensagens do topic `orders` e reconstruir o contexto a partir dos headers W3C, para que o processamento continue no mesmo trace iniciado pelo `OrderService`.

**Why P1**: Sem o span de consumo ligado ao `traceparent` recebido, M2 continua interrompido no publish Kafka do `OrderService`.

**Acceptance Criteria**:

1. WHEN o `ProcessingWorker` receber uma mensagem valida do topic `orders` THEN ele SHALL desserializar `orderId`, `description` e `createdAtUtc`
2. WHEN a mensagem contiver header `traceparent` valido THEN o worker SHALL extrair o contexto pai e iniciar um span manual de consumo com `ActivityKind.Consumer`
3. WHEN o header `tracestate` existir THEN o worker SHALL preserva-lo no contexto atual para uso nos spans seguintes
4. WHEN os headers W3C estiverem ausentes ou invalidos THEN o worker SHALL iniciar um novo trace, registrar o problema em log estruturado e continuar o processamento
5. WHEN o processamento da mensagem comecar THEN o span de consumo SHALL registrar tags de mensageria suficientes para identificar topic, operacao e chave da mensagem

**Independent Test**: Publicar ou consumir uma mensagem real do topic `orders`, verificar no log do `processing-worker` o `orderId` processado e confirmar no Tempo que existe um span de consumo ligado ao trace do `POST /orders` quando o header `traceparent` estiver presente.

---

### P1: Enriquecer o processamento com `GET /orders/{id}` ⭐ MVP

**User Story**: Como `ProcessingWorker`, quero buscar o pedido persistido no `OrderService` antes de produzir a proxima mensagem, para usar um contrato HTTP estavel como fonte de verdade do enriquecimento.

**Why P1**: O evento Kafka atual e enxuto por design; o estado persistido completo vive em `GET /orders/{id}`.

**Acceptance Criteria**:

1. WHEN o span de consumo estiver ativo THEN a chamada `GET /orders/{id}` SHALL acontecer dentro desse contexto, gerando span de cliente HTTP filho
2. WHEN o `OrderService` responder `200 OK` THEN o worker SHALL usar `orderId`, `description`, `status`, `createdAtUtc` e `publishedAtUtc` da resposta para montar a mensagem seguinte
3. WHEN a chamada HTTP for concluida com sucesso THEN o trace SHALL conter spans encadeados de Kafka consumer e HTTP client sob o mesmo `TraceId`
4. WHEN o payload HTTP divergente do `orderId` consumido for detectado THEN o worker SHALL tratar como erro de consistencia, nao publicar em `notifications` e registrar log estruturado
5. WHEN o `OrderService` estiver indisponivel ou a chamada expirar THEN o worker SHALL marcar erro no span atual e nao produzir a mensagem seguinte

**Independent Test**: Criar um pedido via `POST /orders`, aguardar o `ProcessingWorker` consumir o evento e verificar no Tempo que o trace contem o span de cliente HTTP para `GET /orders/{id}` como filho do span de consumo.

---

### P1: Publicar em `notifications` preservando o contexto ⭐ MVP

**User Story**: Como `ProcessingWorker`, quero publicar um evento enriquecido no topic `notifications` mantendo o contexto W3C, para que o proximo hop do milestone possa continuar o mesmo trace.

**Why P1**: M2 so fecha quando o fluxo seguir de forma rastreavel ate o proximo topic.

**Acceptance Criteria**:

1. WHEN o enriquecimento HTTP retornar sucesso THEN o worker SHALL publicar uma mensagem no topic `notifications`
2. WHEN a mensagem for publicada THEN o payload SHALL conter pelo menos `orderId`, `description`, `status`, `createdAtUtc`, `publishedAtUtc` e `processedAtUtc`
3. WHEN o producer Kafka criar a mensagem THEN os headers `traceparent` e, quando disponivel, `tracestate` SHALL ser injetados manualmente
4. WHEN a publicacao ocorrer com sucesso THEN o trace SHALL conter um span manual de producer Kafka filho do span de consumo/processamento
5. WHEN a publicacao Kafka falhar THEN o worker SHALL registrar excecao, marcar erro no span de producer e nao encerrar o processo host

**Independent Test**: Consumir uma mensagem real de `orders`, inspecionar uma mensagem produzida em `notifications` via `kafka-console-consumer` e confirmar a presenca do payload enriquecido e dos headers W3C.

---

### P1: Tornar `404` e falhas HTTP observaveis sem publicar a proxima mensagem ⭐ MVP

**User Story**: Como operador, quero distinguir no trace e nos logs quando o enriquecimento falha por `404` ou por erro tecnico, para entender por que a cadeia parou antes de `notifications`.

**Why P1**: O milestone precisa demonstrar nao apenas o caminho feliz, mas tambem onde o fluxo distribuido para quando ha inconsistencia ou indisponibilidade.

**Acceptance Criteria**:

1. WHEN `GET /orders/{id}` retornar `404 Not Found` THEN o worker SHALL nao publicar no topic `notifications`
2. WHEN ocorrer `404` THEN o span de consumo/processamento SHALL ser marcado com erro ou status equivalente de falha de negocio e o log SHALL incluir `orderId`, `TraceId`, `SpanId` e `http.status_code = 404`
3. WHEN a chamada HTTP falhar por timeout, rede ou resposta `5xx` THEN o worker SHALL nao publicar no topic `notifications`
4. WHEN ocorrer falha tecnica HTTP THEN o span de consumo/processamento SHALL registrar excecao e o worker SHALL permanecer saudavel para processar novas mensagens
5. WHEN qualquer um desses erros ocorrer THEN retry, backoff e DLQ SHALL permanecer fora de escopo desta feature e isso SHALL ficar explicito na implementacao e na validacao

**Independent Test**: Forcar um `404` consumindo um `orderId` inexistente e, separadamente, simular indisponibilidade do `OrderService`; nos dois cenarios validar ausencia de mensagem em `notifications`, logs estruturados e spans de erro no Tempo.

---

### P2: Logs estruturados com correlacao de trace

**User Story**: Como operador, quero logs do `ProcessingWorker` com `orderId`, `TraceId` e `SpanId`, para correlacionar eventos Kafka, chamada HTTP e falhas no Tempo e no Loki.

**Why P2**: A PoC precisa manter consistencia entre traces e logs, nao apenas spans isolados.

**Acceptance Criteria**:

1. WHEN uma mensagem de `orders` for recebida THEN o log SHALL incluir `orderId` e o topic consumido
2. WHEN o enriquecimento HTTP concluir com sucesso THEN o log SHALL registrar a preparacao ou publicacao em `notifications` com os identificadores do span corrente
3. WHEN ocorrer `404` ou falha tecnica THEN o log SHALL incluir o motivo do erro e os identificadores de correlacao do trace

**Independent Test**: Executar o fluxo feliz e um fluxo de erro, entao verificar nos logs do container `processing-worker` a presenca consistente de `orderId`, `TraceId` e `SpanId`.

## Edge Cases

- WHEN a mensagem Kafka vier sem `orderId` valido THEN o worker SHALL registrar erro e nao tentar o `GET /orders/{id}`
- WHEN o JSON consumido estiver invalido THEN o worker SHALL registrar o payload bruto quando seguro, marcar erro observavel e continuar saudavel
- WHEN `traceparent` existir mas nao puder ser parseado THEN o worker SHALL iniciar um novo trace sem abortar o processamento
- WHEN a chamada HTTP retornar `200` com payload sem `publishedAtUtc` apesar de `status = published` THEN o worker SHALL tratar como inconsistência de contrato e nao publicar em `notifications`
- WHEN o producer Kafka de `notifications` falhar apos o enriquecimento bem-sucedido THEN o trace SHALL expor claramente que o fluxo parou no hop de producer

## Tempo Validation Criteria

### Happy path

1. Criar um pedido real com `POST /orders`
2. Confirmar que o `OrderService` publicou em `orders` com `traceparent`
3. Confirmar no Tempo um unico trace contendo, no minimo:
   - span root HTTP `POST /orders` no `order-service`
   - span manual `kafka publish orders` no `order-service`
   - span manual `kafka consume orders` no `processing-worker`
   - span de cliente HTTP `GET /orders/{id}` no `processing-worker`
   - span manual `kafka publish notifications` no `processing-worker`
4. Confirmar que os spans do `processing-worker` compartilham o mesmo `TraceId` do span root do `OrderService`

### Error path: 404

1. Produzir ou reenfileirar mensagem com `orderId` inexistente
2. Confirmar no Tempo um trace contendo o span `kafka consume orders` e o span HTTP `GET /orders/{id}` com `404`
3. Confirmar ausencia de span `kafka publish notifications` para esse processamento

### Error path: falha tecnica HTTP

1. Tornar o `OrderService` indisponivel ou induzir timeout controlado
2. Confirmar no Tempo o span de consumo com erro e o span HTTP com excecao ou status de falha
3. Confirmar ausencia de span de producer para `notifications`

## Success Criteria

- [ ] O `ProcessingWorker` consome `orders` e reconstrói o contexto distribuido a partir de `traceparent` e `tracestate`
- [ ] O trace do fluxo feliz mostra Kafka consumer -> HTTP client -> Kafka producer no mesmo `TraceId`
- [ ] O topic `notifications` recebe payload enriquecido minimo com propagacao W3C preservada
- [ ] Respostas `404` e falhas HTTP nao publicam a proxima mensagem e ficam claramente observaveis em trace e logs
- [ ] O worker permanece saudavel apos erros de enriquecimento ou publicacao