# OpenTelemetry Bootstrap — Tasks

**Design**: `.specs/features/otel-bootstrap/design.md`
**Status**: Done

Validação final: T1-T8 concluídas. Os workers passaram a emitir spans manuais de heartbeat em M1, o que permitiu confirmar os três `service.name` no Tempo sem antecipar a lógica de negócio de M2.

---

## Execution Plan

### Phase 1: OrderService (Sequencial)

```
T1 (OtelExtensions OrderService) → T2 (Program.cs OrderService)
```

### Phase 2: Workers (Paralelo OK após T1 — padrão idêntico)

```
T1 ──┬──→ T3 (OtelExtensions ProcessingWorker) ──→ T4 (Program.cs ProcessingWorker)
     └──→ T5 (OtelExtensions NotificationWorker) ──→ T6 (Program.cs NotificationWorker)
```

### Phase 3: Validação end-to-end

```
T2 + T4 + T6 ──→ T7 (docker-compose up) ──→ T8 (validar no Tempo)
```

---

## Task Breakdown

### T1: Criar OtelExtensions para OrderService

**What**: Criar `src/OrderService/Extensions/OtelExtensions.cs` com o método `AddOtelInstrumentation()` configurando `TracerProvider` com `AddAspNetCoreInstrumentation()`, `AddHttpClientInstrumentation()`, Resource com `service.name`/`service.version` e OTLP gRPC exporter conforme design
**Where**: `src/OrderService/Extensions/OtelExtensions.cs`
**Depends on**: T1 da feature `dotnet-solution` (projeto precisa existir)
**Reuses**: Design de `OtelExtensions` definido em `design.md`

**Done when**:
- [ ] Arquivo existe em `src/OrderService/Extensions/OtelExtensions.cs`
- [ ] Método `AddOtelInstrumentation(IServiceCollection, IConfiguration)` está implementado
- [ ] `AddAspNetCoreInstrumentation()` está incluído
- [ ] `AddHttpClientInstrumentation()` está incluído
- [ ] OTLP exporter configurado com protocolo gRPC
- [ ] Resource usa `OTEL_SERVICE_NAME` da configuração com fallback `"order-service"`
- [ ] `dotnet build src/OrderService/OrderService.csproj` sem erros
- [x] Arquivo existe em `src/OrderService/Extensions/OtelExtensions.cs`
- [x] Método `AddOtelInstrumentation(IServiceCollection, IConfiguration)` está implementado
- [x] `AddAspNetCoreInstrumentation()` está incluído
- [x] `AddHttpClientInstrumentation()` está incluído
- [x] OTLP exporter configurado com protocolo gRPC
- [x] Resource usa `OTEL_SERVICE_NAME` da configuração com fallback `"order-service"`
- [x] `dotnet build src/OrderService/OrderService.csproj` sem erros

---

### T2: Atualizar Program.cs do OrderService para usar OtelExtensions

**What**: Modificar `src/OrderService/Program.cs` para chamar `builder.Services.AddOtelInstrumentation(builder.Configuration)` após a criação do builder
**Where**: `src/OrderService/Program.cs`
**Depends on**: T1

**Done when**:
- [ ] `Program.cs` chama `AddOtelInstrumentation` em no máximo 1 linha
- [ ] Não há configuração OTel inline no `Program.cs` (tudo encapsulado na extensão)
- [ ] `dotnet build` sem erros
- [x] `Program.cs` chama `AddOtelInstrumentation` em no máximo 1 linha
- [x] Não há configuração OTel inline no `Program.cs` (tudo encapsulado na extensão)
- [x] `dotnet build` sem erros

---

### T3: Criar OtelExtensions para ProcessingWorker

**What**: Criar `src/ProcessingWorker/Extensions/OtelExtensions.cs` com o método `AddOtelInstrumentation()` — mesmo padrão do OrderService mas SEM `AddAspNetCoreInstrumentation()`, com fallback de `service.name` para `"processing-worker"`
**Where**: `src/ProcessingWorker/Extensions/OtelExtensions.cs`
**Depends on**: T1 da feature `dotnet-solution` (projeto precisa existir)
**Reuses**: Padrão identical ao T1, removendo a instrumentação AspNetCore

**Done when**:
- [ ] Arquivo existe em `src/ProcessingWorker/Extensions/OtelExtensions.cs`
- [ ] `AddAspNetCoreInstrumentation()` NÃO está incluído
- [ ] `AddHttpClientInstrumentation()` está incluído
- [ ] Resource usa fallback `"processing-worker"`
- [ ] `dotnet build src/ProcessingWorker/ProcessingWorker.csproj` sem erros
- [x] Arquivo existe em `src/ProcessingWorker/Extensions/OtelExtensions.cs`
- [x] `AddAspNetCoreInstrumentation()` NÃO está incluído
- [x] `AddHttpClientInstrumentation()` está incluído
- [x] Resource usa fallback `"processing-worker"`
- [x] `dotnet build src/ProcessingWorker/ProcessingWorker.csproj` sem erros

---

### T4: Atualizar Program.cs do ProcessingWorker para usar OtelExtensions

**What**: Modificar `src/ProcessingWorker/Program.cs` (template worker usa `Host.CreateApplicationBuilder`) para chamar `builder.Services.AddOtelInstrumentation(builder.Configuration)`
**Where**: `src/ProcessingWorker/Program.cs`
**Depends on**: T3

**Done when**:
- [ ] `Program.cs` chama `AddOtelInstrumentation` em no máximo 1 linha
- [ ] `dotnet build` sem erros
- [x] `Program.cs` chama `AddOtelInstrumentation` em no máximo 1 linha
- [x] `dotnet build` sem erros

---

### T5: Criar OtelExtensions para NotificationWorker

**What**: Criar `src/NotificationWorker/Extensions/OtelExtensions.cs` com o mesmo padrão do ProcessingWorker, com fallback de `service.name` para `"notification-worker"`
**Where**: `src/NotificationWorker/Extensions/OtelExtensions.cs`
**Depends on**: T1 da feature `dotnet-solution` (projeto precisa existir)
**Reuses**: Padrão idêntico ao T3

**Done when**:
- [ ] Arquivo existe em `src/NotificationWorker/Extensions/OtelExtensions.cs`
- [ ] `AddAspNetCoreInstrumentation()` NÃO está incluído
- [ ] Resource usa fallback `"notification-worker"`
- [ ] `dotnet build src/NotificationWorker/NotificationWorker.csproj` sem erros
- [x] Arquivo existe em `src/NotificationWorker/Extensions/OtelExtensions.cs`
- [x] `AddAspNetCoreInstrumentation()` NÃO está incluído
- [x] Resource usa fallback `"notification-worker"`
- [x] `dotnet build src/NotificationWorker/NotificationWorker.csproj` sem erros

---

### T6: Atualizar Program.cs do NotificationWorker para usar OtelExtensions

**What**: Modificar `src/NotificationWorker/Program.cs` para chamar `builder.Services.AddOtelInstrumentation(builder.Configuration)`
**Where**: `src/NotificationWorker/Program.cs`
**Depends on**: T5

**Done when**:
- [ ] `Program.cs` chama `AddOtelInstrumentation` em no máximo 1 linha
- [ ] `dotnet build` sem erros
- [x] `Program.cs` chama `AddOtelInstrumentation` em no máximo 1 linha
- [x] `dotnet build` sem erros

---

### T7: Smoke test — docker-compose up com OTel ativo

**What**: Executar `docker-compose up -d` após aplicar todas as mudanças de OTel e verificar que os 3 serviços sobem sem erro relacionado ao OTel
**Where**: Execução local (não cria arquivo)
**Depends on**: T2, T4, T6

**Done when**:
- [ ] `docker-compose up -d` conclui sem erro
- [ ] Logs dos serviços NÃO contêm `OpenTelemetry` exception ou `OTLP export failed` em nível FATAL
- [ ] Logs do `otelcol` mostram spans sendo recebidos (`"Received"` no pipeline de traces) ao menos do startup e das requests básicas do `OrderService`
- [x] `docker-compose up -d` conclui sem erro
- [x] Logs dos serviços NÃO contêm `OpenTelemetry` exception ou `OTLP export failed` em nível FATAL
- [x] Logs do `otelcol` mostram spans sendo recebidos no pipeline de traces após requests básicas do `OrderService`

---

### T8: Validar 3 services no Grafana Tempo

**What**: Abrir o Grafana Tempo (http://localhost:3000), aplicar filtro por `service.name` e confirmar que cada serviço aparece como source distinto
**Where**: Execução local no browser (não cria arquivo)
**Depends on**: T7

**Done when**:
- [ ] `service.name = order-service` retorna spans no Tempo
- [ ] `service.name = processing-worker` retorna spans no Tempo
- [ ] `service.name = notification-worker` retorna spans no Tempo
- [ ] Health checks NÃO aparecem nos resultados (filtrados pelo processor `drop-health-checks`)
- [x] `service.name = order-service` retorna spans no Tempo
- [x] `service.name = processing-worker` retorna spans no Tempo
- [x] `service.name = notification-worker` retorna spans no Tempo
- [x] Health checks NÃO aparecem nos resultados recentes após o ajuste do processor `drop-health-checks`

Resultado da validação: o Tempo retornou traces recentes para `order-service`, `processing-worker` e `notification-worker`; as buscas recentes para `service.name=order-service url.path=/health` seguiram vazias.

---

## Parallel Execution Map

```
Phase 1:
  T1 ──→ T2

Phase 2 (paralelo, mesmo padrão do T1):
  T1 ──┬──→ T3 ──→ T4
       └──→ T5 ──→ T6

Phase 3 (após todos os Program.cs):
  T2 + T4 + T6 ──→ T7 ──→ T8
```
