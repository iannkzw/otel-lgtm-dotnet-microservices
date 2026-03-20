# OrderService API e Persistência — Specification

**Milestone**: M2 — Fluxo de Eventos End-to-End
**Status**: Specified

---

## Problem Statement

O `OrderService` ainda expõe apenas endpoints de prontidão e health. Para iniciar M2 de forma útil, a API precisa aceitar a criação de pedidos, persisti-los no PostgreSQL e publicar o evento correspondente no Kafka, de modo que um `POST /orders` passe a ser o root span real do fluxo distribuído que depois seguirá por `ProcessingWorker` e `NotificationWorker`.

Além disso, o milestone já antecipa que o `ProcessingWorker` fará um `HTTP GET /orders/{id}` para enriquecer o processamento. Isso exige que o contrato de leitura do pedido seja estável desde agora, com dados persistidos e observáveis via traces, logs e spans de banco.

## Goals

- [ ] Implementar `POST /orders` em Minimal API com payload mock e resposta determinística
- [ ] Persistir o pedido no PostgreSQL antes da publicação do evento Kafka
- [ ] Implementar `GET /orders/{id}` retornando o registro persistido para consumo futuro do `ProcessingWorker`
- [ ] Publicar evento no topic Kafka `orders` com propagação manual de `traceparent` e `tracestate`
- [ ] Tornar a persistência observável com spans de EF Core/Npgsql dentro do mesmo trace da request HTTP
- [ ] Registrar logs estruturados do fluxo de criação e erro com correlação de trace

## Out of Scope

- Implementação do consumer Kafka no `ProcessingWorker`
- Implementação do `HTTP GET` no `ProcessingWorker`
- Implementação do consumer e persistência do `NotificationWorker`
- Dashboard, métricas customizadas e alertas de M3
- Retry, DLQ, transactional outbox e garantias avançadas de entrega
- Regras de negócio reais, autenticação ou validações complexas de domínio

---

## API Contract

### POST /orders

**Request body**:

```json
{
  "description": "demo order"
}
```

**Success response** (`201 Created`):

```json
{
  "orderId": "3f5f6f3d-4d6f-4f19-b3de-1c7617a363a4",
  "description": "demo order",
  "status": "published",
  "createdAtUtc": "2026-03-19T18:30:00.0000000+00:00",
  "publishedAtUtc": "2026-03-19T18:30:00.1500000+00:00"
}
```

**Error response shape**: `ProblemDetails`

### GET /orders/{id}

**Success response** (`200 OK`):

```json
{
  "orderId": "3f5f6f3d-4d6f-4f19-b3de-1c7617a363a4",
  "description": "demo order",
  "status": "published",
  "createdAtUtc": "2026-03-19T18:30:00.0000000+00:00",
  "publishedAtUtc": "2026-03-19T18:30:00.1500000+00:00"
}
```

**Not found**: `404 Not Found`

---

## User Stories

### P1: Criar pedido com persistência local e publicação Kafka ⭐ MVP

**User Story**: Como consumidor da API, quero enviar um `POST /orders` simples e receber um identificador persistido, para que o fluxo de processamento assíncrono comece a partir de um pedido já gravado no banco.

**Why P1**: Sem persistência e publicação no `OrderService`, M2 não tem root span nem evento para iniciar o fluxo distribuído.

**Acceptance Criteria**:

1. WHEN um `POST /orders` válido chega ao `OrderService` THEN a API SHALL gerar um `orderId` único e `createdAtUtc` em UTC
2. WHEN o pedido é criado THEN o `OrderService` SHALL persistir o registro no PostgreSQL antes da publicação Kafka
3. WHEN a persistência e a publicação Kafka concluem com sucesso THEN a API SHALL retornar `201 Created`, incluir header `Location: /orders/{id}` e responder com `status = published`
4. WHEN a publicação Kafka falha após a persistência THEN o registro SHALL permanecer salvo com `status = publish_failed`, o span da request SHALL ser marcado com erro e a API SHALL responder `503 Service Unavailable`
5. WHEN a persistência no PostgreSQL falha THEN a API SHALL NÃO publicar a mensagem no Kafka e SHALL responder erro 5xx

**Independent Test**: Executar `POST /orders`, verificar `201`, consultar a tabela `orders` no PostgreSQL e confirmar que o topic `orders` recebeu um evento com o mesmo `orderId`.

---

### P1: Expor leitura consistente do pedido para o fluxo futuro do ProcessingWorker ⭐ MVP

**User Story**: Como `ProcessingWorker`, quero consultar `GET /orders/{id}` e receber o pedido persistido, para que o enriquecimento via HTTP em M2 use um contrato estável e previsível.

**Why P1**: O próximo passo do milestone depende dessa rota existir e refletir exatamente o estado armazenado no banco.

**Acceptance Criteria**:

1. WHEN `GET /orders/{id}` recebe um `Guid` existente THEN a API SHALL retornar `200 OK` com os campos `orderId`, `description`, `status`, `createdAtUtc` e `publishedAtUtc`
2. WHEN o `orderId` não existe THEN a API SHALL retornar `404 Not Found`
3. WHEN a leitura é bem-sucedida THEN a resposta SHALL refletir o estado persistido no PostgreSQL, sem reconstrução em memória
4. WHEN o pedido está com `status = publish_failed` THEN o `GET` SHALL retornar esse estado sem mascará-lo

**Independent Test**: Criar um pedido via `POST /orders` e ler o mesmo `orderId` via `GET /orders/{id}` comparando os dados retornados com o registro persistido.

---

### P1: Tornar a persistência observável no trace HTTP → DB → Kafka ⭐ MVP

**User Story**: Como engenheiro de observabilidade, quero que um `POST /orders` gere spans de HTTP, banco e publicação Kafka no mesmo trace, para que o caminho completo até a emissão do evento seja visível no Tempo.

**Why P1**: M2 existe para demonstrar tracing distribuído real; sem spans de banco e sem contexto Kafka a demo fica incompleta.

**Acceptance Criteria**:

1. WHEN `POST /orders` executa uma inserção no PostgreSQL THEN o trace SHALL conter span de cliente DB com `db.system = postgresql`
2. WHEN `GET /orders/{id}` lê o banco THEN o trace SHALL conter span de cliente DB correspondente à consulta
3. WHEN o `OrderService` publica no Kafka THEN SHALL existir um span manual filho da request HTTP representando a publicação do evento
4. WHEN a publicação Kafka ocorre THEN os headers `traceparent` e, quando disponível, `tracestate` SHALL ser enviados junto com a mensagem
5. WHEN o `ProcessingWorker` consumir a mensagem futuramente THEN ele SHALL conseguir reconstruir o contexto pai usando apenas os headers enviados pelo `OrderService`

**Independent Test**: Fazer `POST /orders`, abrir o trace no Tempo e confirmar a presença do span HTTP root, do span de insert em PostgreSQL e do span manual de publish Kafka no mesmo `TraceId`.

---

### P2: Registrar logs estruturados com correlação de trace

**User Story**: Como operador, quero logs do `OrderService` com `orderId`, `TraceId` e `SpanId`, para correlacionar falhas de persistência e publicação com o trace correspondente.

**Why P2**: A observabilidade da PoC não se limita a traces; os logs precisam apontar para o mesmo contexto.

**Acceptance Criteria**:

1. WHEN um pedido é criado com sucesso THEN o log SHALL incluir `orderId` e o estado final `published`
2. WHEN a publicação Kafka falha THEN o log SHALL incluir `orderId`, exceção e identificadores do span atual
3. WHEN `GET /orders/{id}` retorna `404` THEN o log SHALL registrar o `orderId` consultado com nível compatível ao caso esperado

**Independent Test**: Executar `POST /orders` e `GET /orders/{id}` e verificar nos logs do container do `order-service` a presença de `orderId` e identificadores de trace/span.

---

## Edge Cases

- WHEN o payload de `POST /orders` vem sem `description` ou apenas com whitespace THEN a API SHALL retornar `400 Bad Request`
- WHEN `GET /orders/{id}` recebe um identificador fora do formato `Guid` THEN a rota SHALL retornar `400 Bad Request` ou não casar com o endpoint tipado
- WHEN o PostgreSQL está indisponível no startup ou em runtime THEN a API SHALL permanecer íntegra no processo, mas o endpoint SHALL falhar sem tentar publicar no Kafka
- WHEN o Kafka está indisponível após a gravação do pedido THEN o estado persistido SHALL indicar `publish_failed` para tornar a inconsistência observável
- WHEN o mesmo cliente repetir a chamada por timeout THEN idempotência NÃO SHALL ser garantida nesta feature

## Success Criteria

- [ ] `POST /orders` persiste dados no PostgreSQL e retorna `201 Created` em caso de sucesso
- [ ] `GET /orders/{id}` lê exclusivamente do PostgreSQL e suporta o contrato futuro do `ProcessingWorker`
- [ ] O trace de `POST /orders` mostra spans conectados de HTTP, DB e publish Kafka
- [ ] O evento publicado no topic `orders` contém os headers W3C de trace context
- [ ] Falhas de Kafka ficam explícitas no banco (`publish_failed`) e nos logs