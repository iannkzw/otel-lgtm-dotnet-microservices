# OrderService API e Persistência — Tasks

**Design**: `.specs/features/order-service-api-persistencia/design.md`
**Status**: Implemented and Validated

Implementação concluída no `OrderService` com validação local via Docker Compose, PostgreSQL, Kafka e Tempo.

---

## Execution Plan

### Phase 1: Fundamentos de persistência

```
T1 (pacotes/configuração) → T2 (modelo + DbContext) → T3 (bootstrap do schema)
```

### Phase 2: API e publicação de eventos

```
T3 ──→ T4 (DTOs + mapeamento de endpoints) ──→ T5 (publisher Kafka)
                                      └────→ T6 (helper de tracing Kafka)
```

### Phase 3: Observabilidade e validação

```
T4 + T5 + T6 ──→ T7 (OTel + logs) ──→ T8 (smoke test compose) ──→ T9 (validar trace e dados)
```

---

## Task Breakdown

### T1: Adicionar dependências de persistência e mensageria ao OrderService

**What**: Atualizar `OrderService.csproj` para incluir EF Core PostgreSQL, instrumentação EF Core e cliente Kafka.
**Where**: `src/OrderService/OrderService.csproj`
**Depends on**: feature `otel-bootstrap` concluída

**Done when**:
- [x] `Npgsql.EntityFrameworkCore.PostgreSQL` está referenciado
- [x] `OpenTelemetry.Instrumentation.EntityFrameworkCore` está referenciado
- [x] `Confluent.Kafka` está referenciado
- [x] `dotnet build src/OrderService/OrderService.csproj` passa sem erros

---

### T2: Criar modelo `Order`, DTOs e `OrderDbContext`

**What**: Implementar a entidade persistida, os contratos HTTP e o contexto EF Core do `OrderService`.
**Where**: `src/OrderService/Data/`, `src/OrderService/Contracts/` ou equivalente
**Depends on**: T1

**Done when**:
- [x] Existe uma entidade `Order` com `Id`, `Description`, `Status`, `CreatedAtUtc` e `PublishedAtUtc`
- [x] Existe um `OrderDbContext` configurado para a tabela `orders`
- [x] Existem DTOs para request/response de `POST /orders` e `GET /orders/{id}`
- [x] O mapeamento de colunas preserva nomes coerentes para PostgreSQL

---

### T3: Registrar `DbContext` e bootstrapar o schema no startup

**What**: Registrar o `OrderDbContext` com `POSTGRES_CONNECTION_STRING` e garantir criação automática do schema em ambiente limpo.
**Where**: `src/OrderService/Program.cs` e componentes de startup relacionados
**Depends on**: T2

**Done when**:
- [x] O `DbContext` usa `POSTGRES_CONNECTION_STRING`
- [x] O startup executa bootstrap mínimo do schema da tabela `orders`
- [x] O serviço sobe em compose limpo sem etapa manual de DDL
- [x] Falha de conexão com banco aparece de forma clara nos logs

---

### T4: Implementar os endpoints `POST /orders` e `GET /orders/{id}`

**What**: Mapear os dois endpoints em Minimal API, com validação de payload, leitura/escrita real no PostgreSQL e respostas conforme a spec.
**Where**: `src/OrderService/Program.cs` ou arquivo dedicado de endpoints
**Depends on**: T3

**Done when**:
- [x] `POST /orders` valida `description` obrigatória
- [x] `POST /orders` cria um pedido com `status = pending_publish` antes do publish Kafka
- [x] `GET /orders/{id}` consulta o banco e retorna `404` quando necessário
- [x] `Location: /orders/{id}` é retornado no `201 Created`

---

### T5: Implementar publisher Kafka para o topic `orders`

**What**: Criar um publisher dedicado para enviar o evento `OrderCreatedEvent` ao Kafka após a persistência local.
**Where**: `src/OrderService/Messaging/`
**Depends on**: T4

**Done when**:
- [x] O topic usado é `orders` por padrão ou `KAFKA_TOPIC_ORDERS` quando definido
- [x] O payload publicado contém `orderId`, `description` e `createdAtUtc`
- [x] Falha de publish atualiza o pedido para `publish_failed`
- [x] Sucesso de publish atualiza o pedido para `published` e `publishedAtUtc`

---

### T6: Implementar `KafkaTracingHelper` reutilizável

**What**: Criar um helper para injetar `traceparent` e `tracestate` nos headers Kafka, desenhado para reaproveitamento nos workers futuros.
**Where**: `src/OrderService/Messaging/` ou pasta compartilhada equivalente
**Depends on**: T5

**Done when**:
- [x] Headers Kafka recebem `traceparent` quando existe `Activity.Current`
- [x] `tracestate` é incluído quando disponível
- [x] O código de injeção fica isolado em helper reutilizável
- [x] O publisher Kafka não monta headers W3C inline

---

### T7: Expandir observabilidade do OrderService para banco, Kafka e logs correlacionados

**What**: Ajustar o bootstrap OTel e os logs do `OrderService` para refletir o fluxo completo HTTP → DB → Kafka.
**Where**: `src/OrderService/Extensions/OtelExtensions.cs` e pontos de logging da API/publisher
**Depends on**: T4, T5, T6

**Done when**:
- [x] `AddEntityFrameworkCoreInstrumentation()` está configurado
- [x] Existe `ActivitySource` manual para spans de aplicação/publish Kafka
- [x] Logs de sucesso e falha incluem `orderId`
- [x] Logs relevantes conseguem ser correlacionados com `TraceId` e `SpanId`

---

### T8: Smoke test do fluxo local via Docker Compose

**What**: Subir o ambiente, chamar a API e confirmar que o `OrderService` persiste e publica sem quebrar a baseline de M1.
**Where**: execução local
**Depends on**: T7

**Done when**:
- [x] `docker compose up -d --build order-service kafka postgres otelcol` conclui sem erro funcional novo
- [x] `POST /orders` retorna `201` em cenário saudável
- [x] `GET /orders/{id}` retorna `200` para o pedido recém-criado
- [x] M1 segue preservado: collector operacional e `order-service` continua exportando spans

---

### T9: Validar observabilidade e persistência fim a fim

**What**: Confirmar a coerência entre API, PostgreSQL, Kafka e Tempo após os smoke tests.
**Where**: Grafana Tempo, logs e inspeção dos dados persistidos
**Depends on**: T8

**Done when**:
- [x] O trace de `POST /orders` mostra spans de HTTP, PostgreSQL e publish Kafka no mesmo `TraceId`
- [x] A tabela `orders` contém o registro criado com o estado esperado
- [x] O evento enviado ao topic `orders` contém headers W3C de trace context
- [x] Falha simulada de Kafka marca o pedido como `publish_failed` e aparece como erro observável

---

## Parallel Execution Map

```
Phase 1:
  T1 ──→ T2 ──→ T3

Phase 2:
  T3 ──→ T4 ──→ T5
               └──→ T6

Phase 3:
  T4 + T5 + T6 ──→ T7 ──→ T8 ──→ T9
```