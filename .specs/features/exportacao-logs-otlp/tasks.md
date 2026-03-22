# Exportação de Logs OTLP — Tasks

**Design**: `.specs/features/exportacao-logs-otlp/design.md`
**Status**: Implemented — build succeeded (0 errors, 0 warnings)

---

## Execution Plan

### Phase 1: Fix do ProcessingWorker (Sequencial — pré-requisito de observabilidade)

```
T1
```

### Phase 2: Bootstrap de logs OTel nos 3 serviços (Paralelo após T1)

```
     ┌──→ T2 (OrderService)       ──┐
T1 ──┼──→ T3 (ProcessingWorker)   ──┼──→ T5
     └──→ T4 (NotificationWorker) ──┘
```

### Phase 3: Build e validação end-to-end (Sequencial)

```
T5 → T6
```

---

## Task Breakdown

---

### T1: Corrigir guarda de status no `HandleLookupOutcome` do ProcessingWorker

**What**: Adicionar validação explícita que rejeita pedidos com status diferente de `published` antes de tentar acessar `PublishedAtUtc`, eliminando a `InvalidOperationException` atual.

**Where**: `src/ProcessingWorker/Worker.cs` — método `HandleLookupOutcome`, logo antes do bloco `if (string.Equals(order.Status, "published", ...) && order.PublishedAtUtc is null)`

**Depends on**: None

**Reuses**: Padrão já existente no mesmo método: `result = ProcessingResults.InvalidPayload`, `activity?.SetTag(...)`, `activity?.SetStatus(...)`, `logger.LogError(...)`, `return false`

**Tools**:
- MCP: NONE
- Skill: coding-guidelines

**Done when**:
- [ ] O bloco `if (!string.Equals(order.Status, "published", StringComparison.OrdinalIgnoreCase))` aparece antes da checagem de `publishedAtUtc`
- [ ] O log emitido contém `Classification`, `OrderId`, `TraceId` e `SpanId`
- [ ] O span é marcado com erro e tag `error.type = "order_not_published"`
- [ ] `result` recebe `ProcessingResults.InvalidPayload` (ou valor semântico equivalente)
- [ ] O build da solution em container SDK 10 passa sem erros
- [ ] Nos logs do `processing-worker` após fluxo com pedido `pending_publish`, nenhum `InvalidOperationException` aparece

**Verify**:
```powershell
# Subir a stack sem rebuild de imagem (apenas para teste local):
# 1. Criar um pedido:
$order = Invoke-RestMethod -Uri http://localhost:8080/orders -Method POST `
  -ContentType 'application/json' `
  -Body '{"description":"test-fix"}'
# 2. Checar logs do worker — não deve aparecer "Nullable object must have a value"
docker compose logs --no-color --tail=30 processing-worker
```
Resultado esperado: log de warning/error com mensagem sobre `order_not_published` ou ausência total de `InvalidOperationException`.

---

### T2: Adicionar provider de logs OTel ao `OrderService` [P]

**What**: Registrar `services.AddLogging(logging => logging.AddOpenTelemetry(...))` em `OtelExtensions.AddOtelInstrumentation` do `OrderService`, reutilizando `otlpEndpoint` e `resourceBuilder` já existentes.

**Where**: `src/OrderService/Extensions/OtelExtensions.cs` — final do método `AddOtelInstrumentation`, após o bloco `AddOpenTelemetry()...WithMetrics(...)`

**Depends on**: T1 (não há dependência técnica, mas T1 melhora a observabilidade geral antes do build)

**Reuses**:
- `otlpEndpoint` (linha 17 do arquivo atual)
- `resourceBuilder` (linha 20)

**Tools**:
- MCP: NONE
- Skill: coding-guidelines

**Done when**:
- [ ] `services.AddLogging(logging => logging.AddOpenTelemetry(...))` adicionado após `.WithMetrics(...)`
- [ ] `options.SetResourceBuilder(resourceBuilder)` presente
- [ ] `options.AddOtlpExporter(...)` aponta para `otlpEndpoint` com protocolo gRPC
- [ ] `options.IncludeFormattedMessage = true` e `options.IncludeScopes = true` presentes
- [ ] Nenhum `using` duplicado adicionado (os namespaces necessários já existem)
- [ ] O build da solution em container SDK 10 passa sem erros

**Verify**:
```
# Na fase de validação end-to-end (T6), confirmar que logs do order-service chegam ao Loki.
# Verificação parcial de build:
docker run --rm -v ${PWD}:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build otel-poc.sln
```

---

### T3: Adicionar provider de logs OTel ao `ProcessingWorker` [P]

**What**: Registrar `services.AddLogging(logging => logging.AddOpenTelemetry(...))` em `OtelExtensions.AddOtelInstrumentation` do `ProcessingWorker`.

**Where**: `src/ProcessingWorker/Extensions/OtelExtensions.cs` — final do método `AddOtelInstrumentation`, após o bloco `.WithMetrics(...)`

**Depends on**: T1

**Reuses**:
- `otlpEndpoint` (linha 17 do arquivo)
- `resourceBuilder` (linha 20)

**Tools**:
- MCP: NONE
- Skill: coding-guidelines

**Done when**:
- [ ] `services.AddLogging(logging => logging.AddOpenTelemetry(...))` adicionado após `.WithMetrics(...)`
- [ ] `options.SetResourceBuilder(resourceBuilder)` presente
- [ ] `options.AddOtlpExporter(...)` aponta para `otlpEndpoint` com protocolo gRPC
- [ ] `options.IncludeFormattedMessage = true` e `options.IncludeScopes = true` presentes
- [ ] O build da solution em container SDK 10 passa sem erros

**Verify**: idem T2

---

### T4: Adicionar provider de logs OTel ao `NotificationWorker` [P]

**What**: Registrar `services.AddLogging(logging => logging.AddOpenTelemetry(...))` em `OtelExtensions.AddOtelInstrumentation` do `NotificationWorker`.

**Where**: `src/NotificationWorker/Extensions/OtelExtensions.cs` — final do método `AddOtelInstrumentation`, após o bloco `.WithMetrics(...)`

**Depends on**: T1

**Reuses**:
- `otlpEndpoint` (linha 17 do arquivo)
- `resourceBuilder` (linha 20)

**Tools**:
- MCP: NONE
- Skill: coding-guidelines

**Done when**:
- [ ] `services.AddLogging(logging => logging.AddOpenTelemetry(...))` adicionado após `.WithMetrics(...)`
- [ ] `options.SetResourceBuilder(resourceBuilder)` presente
- [ ] `options.AddOtlpExporter(...)` aponta para `otlpEndpoint` com protocolo gRPC
- [ ] `options.IncludeFormattedMessage = true` e `options.IncludeScopes = true` presentes
- [ ] O build da solution em container SDK 10 passa sem erros

**Verify**: idem T2

---

### T5: Build e rebuild das imagens Docker

**What**: Executar o build da solution em container SDK 10 e o `docker compose build` para garantir que as imagens refletem as alterações antes da validação end-to-end.

**Where**: Raiz do repositório — `otel-poc.sln`, `docker-compose.yaml`

**Depends on**: T2, T3, T4

**Reuses**: Comando canônico documentado no ROADMAP (build em container SDK 10)

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [ ] `docker run --rm -v ${PWD}:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build otel-poc.sln` retorna `Build succeeded` sem erros
- [ ] `docker compose build` completa sem erros para os três serviços
- [ ] Nenhum warning de build relacionado a logs ou OpenTelemetry aparece

**Verify**:
```powershell
docker run --rm -v ${PWD}:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build otel-poc.sln
docker compose build order-service processing-worker notification-worker
```

---

### T6: Validação end-to-end — logs chegando ao Loki

**What**: Subir a stack com as novas imagens, gerar tráfego real e confirmar que os logs dos três serviços chegam ao OTel Collector e ao Loki.

**Where**: Ambiente Docker Compose local

**Depends on**: T5

**Reuses**:
- Script `tools/load-generator/generate-orders.ps1`
- Grafana em `http://localhost:3000`

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [ ] Logs do `otelcol` exibem eventos do tipo `Logs` após geração de tráfego
- [ ] Query `{service_name="order-service"}` no Grafana/Loki retorna resultados
- [ ] Query `{service_name="processing-worker"}` no Grafana/Loki retorna resultados
- [ ] Query `{service_name="notification-worker"}` no Grafana/Loki retorna resultados
- [ ] Ao menos um registro no Loki contém um campo `TraceId` não vazio
- [ ] Nenhum `InvalidOperationException: Nullable object must have a value` nos logs do `processing-worker`
- [ ] Logs locais continuam visíveis via `docker compose logs` (provider de console não foi removido)

**Verify**:
```powershell
# 1. Subir a stack atualizada
docker compose up -d

# 2. Gerar carga real
powershell -File .\tools\load-generator\generate-orders.ps1 -Count 10

# 3. Confirmar chegada de logs no collector
docker compose logs --no-color --tail=50 otelcol | Select-String "Logs"

# 4. Abrir Grafana e consultar Loki
# URL: http://localhost:3000 → Explore → datasource: Loki
# Query: {service_name="order-service"}
# Resultado esperado: ao menos 1 linha de log

# 5. Confirmar ausência do bug no processing-worker
docker compose logs --no-color --tail=50 processing-worker | Select-String "Nullable"
# Resultado esperado: sem matches
```

---

## Parallel Execution Map

```
Phase 1 (Sequencial):
  T1 ──────────────────────────────────────────────────────────→

Phase 2 (Paralelo após T1):
  T1 complete, então:
    ├── T2 [P]  (OrderService)
    ├── T3 [P]  (ProcessingWorker)   } Independentes, podem rodar simultaneamente
    └── T4 [P]  (NotificationWorker)

Phase 3 (Sequencial):
  T2 + T3 + T4 complete:
    T5 ──→ T6
```

---

## Task Granularity Check

| Task | Escopo | Status |
| --- | --- | --- |
| T1: Fix HandleLookupOutcome | 1 função, 1 arquivo | ✅ Granular |
| T2: Logs OTel — OrderService | 1 arquivo, 1 bloco de configuração | ✅ Granular |
| T3: Logs OTel — ProcessingWorker | 1 arquivo, 1 bloco de configuração | ✅ Granular |
| T4: Logs OTel — NotificationWorker | 1 arquivo, 1 bloco de configuração | ✅ Granular |
| T5: Build e rebuild Docker | 2 comandos no shell | ✅ Granular (integração) |
| T6: Validação end-to-end | Validação funcional da feature | ✅ Granular (validação) |
