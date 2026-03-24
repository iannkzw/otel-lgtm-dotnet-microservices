# Tasks — exemplars-metricas-traces

**Status:** Pronto para implementação
**Milestone:** M5 — Correlação Observabilidade
**Criado em:** 2026-03-23

---

## Dependências entre Tasks

```
T1 → T2 → T3 → T4 → T5 → T6(verificação)
             ↗
T7(verificação OTel Collector — paralela a T1-T3)
```

T1, T2 e T3 podem ser executadas em paralelo entre si (3 arquivos independentes).
T4 e T5 dependem que T1-T3 completem (para que o build valide tudo junto).
T7 é verificação pontual isolada, pode ser feita a qualquer momento.

---

## T1 — Habilitar ExemplarFilter no OrderService

**Arquivo:** `src/OrderService/Extensions/OtelExtensions.cs`

**O que fazer:** Adicionar `.SetExemplarFilter(ExemplarFilterType.AlwaysOn)` na cadeia `.WithMetrics()`, após `.AddMeter(OrderMetrics.MeterName)` e antes de `.AddOtlpExporter(...)`.

**Diff esperado:**
```csharp
// Em OtelExtensions.cs do OrderService, dentro de WithMetrics(builder => builder):
.AddMeter(OrderMetrics.MeterName)
.SetExemplarFilter(ExemplarFilterType.AlwaysOn)   // ← adicionar esta linha
.AddOtlpExporter(options =>
```

**Done when:**
- O método `.SetExemplarFilter(ExemplarFilterType.AlwaysOn)` está presente no `WithMetrics()` do `OrderService`
- Não há `using` novo necessário (`ExemplarFilterType` está em `OpenTelemetry.Metrics`, já importado)

**Verificar:** `grep -n "SetExemplarFilter" src/OrderService/Extensions/OtelExtensions.cs`

---

## T2 — Habilitar ExemplarFilter no ProcessingWorker

**Arquivo:** `src/ProcessingWorker/Extensions/OtelExtensions.cs`

**O que fazer:** Idêntico a T1, dentro do `WithMetrics()` do `ProcessingWorker`.

**Diff esperado:**
```csharp
.AddMeter(ProcessingMetrics.MeterName)
.SetExemplarFilter(ExemplarFilterType.AlwaysOn)   // ← adicionar esta linha
.AddOtlpExporter(options =>
```

**Done when:**
- `.SetExemplarFilter(ExemplarFilterType.AlwaysOn)` presente no `WithMetrics()` do `ProcessingWorker`

**Verificar:** `grep -n "SetExemplarFilter" src/ProcessingWorker/Extensions/OtelExtensions.cs`

---

## T3 — Habilitar ExemplarFilter no NotificationWorker

**Arquivo:** `src/NotificationWorker/Extensions/OtelExtensions.cs`

**O que fazer:** Idêntico a T1 e T2, dentro do `WithMetrics()` do `NotificationWorker`.

**Diff esperado:**
```csharp
.AddMeter(NotificationMetrics.MeterName)
.SetExemplarFilter(ExemplarFilterType.AlwaysOn)   // ← adicionar esta linha
.AddOtlpExporter(options =>
```

**Done when:**
- `.SetExemplarFilter(ExemplarFilterType.AlwaysOn)` presente no `WithMetrics()` do `NotificationWorker`

**Verificar:** `grep -n "SetExemplarFilter" src/NotificationWorker/Extensions/OtelExtensions.cs`

---

## T4 — Criar datasource provisioning com exemplarTraceIdDestinations

**Arquivo:** `grafana/provisioning/datasources/otel-poc-datasource-exemplars.yaml` *(novo arquivo)*

**O que fazer:** Criar o arquivo com o seguinte conteúdo:

```yaml
apiVersion: 1

datasources:
  - name: Prometheus
    uid: prometheus
    type: prometheus
    url: http://localhost:9090
    isDefault: true
    jsonData:
      exemplarTraceIdDestinations:
        - name: traceID
          datasourceUid: tempo
```

**Done when:**
- Arquivo criado em `grafana/provisioning/datasources/otel-poc-datasource-exemplars.yaml`
- Conteúdo tem `uid: prometheus` e `exemplarTraceIdDestinations` → `datasourceUid: tempo`

**Verificar:** `Test-Path grafana/provisioning/datasources/otel-poc-datasource-exemplars.yaml`

---

## T5 — Adicionar bind mount do datasource no docker-compose.yaml

**Arquivo:** `docker-compose.yaml`

**O que fazer:** No serviço `lgtm`, dentro da seção `volumes:`, adicionar a linha de bind mount do novo arquivo de datasource.

**Diff esperado:**
```yaml
  lgtm:
    # ... (volumes existentes) ...
    volumes:
      - ./grafana/provisioning/dashboards/otel-poc-dashboards.yaml:/otel-lgtm/grafana/conf/provisioning/dashboards/otel-poc-dashboards.yaml:ro
      # ... outros volumes existentes ...
      - ./grafana/provisioning/datasources/otel-poc-datasource-exemplars.yaml:/otel-lgtm/grafana/conf/provisioning/datasources/otel-poc-datasource-exemplars.yaml:ro  # ← adicionar
```

**Done when:**
- `docker-compose.yaml` contém o bind mount do arquivo `otel-poc-datasource-exemplars.yaml` para o path `/otel-lgtm/grafana/conf/provisioning/datasources/otel-poc-datasource-exemplars.yaml`

**Verificar:** `Select-String "datasource-exemplars" docker-compose.yaml`

---

## T6 — Build de validação

**Comando:**
```powershell
docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build otel-poc.sln
```
*(ou usar a task `build-solution-sdk10` do workspace)*

**Done when:**
- Build retorna `Build succeeded.`
- `0 Error(s)`
- Nenhum warning novo relacionado a `ExemplarFilter` ou `OpenTelemetry.Metrics`

---

## T7 — Verificar que otelcol.yaml não filtra exemplars

**Arquivo:** `otelcol.yaml`

**O que verificar:** Confirmar que:
1. O pipeline `metrics` **não** tem processor `transform` habitado
2. Os processors `memory_limiter` e `batch` estão presentes e são os únicos (sem `filter` processor)
3. O exporter `otlphttp/metrics` está apontando para `${METRICS_URL}` sem headers de compressão que possam corromper exemplars

**Done when:**
- Pipeline `metrics` em `otelcol.yaml` usa apenas `[memory_limiter, batch]`
- Nenhum `filter` ou `transform` processor está habilitado no pipeline de métricas
- Nenhuma mudança de arquivo é necessária (task de verificação pura)

**Verificar:**
```powershell
Select-String "filter|transform" otelcol.yaml
# Resultado esperado: sem matches no bloco de métricas
```

---

## T8 — Validação end-to-end (pós-deploy)

**Pré-requisitos:** Stack rodando com `docker compose up -d`, load generator executado para gerar requests.

**Passos de validação:**

### Passo 1 — Confirmar que exemplars chegam ao Prometheus
```powershell
# Após subir a stack e gerar alguns requests:
Invoke-RestMethod "http://localhost:9090/api/v1/query_exemplars?query=orders_create_duration_bucket&start=$(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ' -Date (Get-Date).AddMinutes(-10))&end=$(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ')"
```
*Resultado esperado:* JSON com array `data` não-vazio contendo objetos com `labels.traceID`

### Passo 2 — Confirmar UID do datasource Prometheus
```powershell
Invoke-RestMethod "http://localhost:3000/api/datasources" -Headers @{"Authorization"="Basic $(Convert-ToBase64 'admin:admin')"}
# Verificar que uid do datasource Prometheus é "prometheus"
```

### Passo 3 — Verificar ícone de exemplar no Grafana
- Abrir `http://localhost:3000`
- Navegar até o dashboard `OTel PoC`
- Painel `Order P95 Latency` (ou equivalente que use `histogram_quantile(0.95, ...)`)
- Clicar no ícone de régua/diamante de exemplar em um ponto de dados
- Confirmar que o Tempo abre com o trace correto

**Done when:**
- Exemplars confirmados via API do Prometheus
- UID do datasource corresponde ao arquivo de provisioning
- Ícone de exemplar visível no painel de latência
- Click no exemplar abre trace no Tempo

---

## Resumo de Artefatos Modificados / Criados

| Arquivo | Ação |
|---|---|
| `src/OrderService/Extensions/OtelExtensions.cs` | Modificar — +1 linha |
| `src/ProcessingWorker/Extensions/OtelExtensions.cs` | Modificar — +1 linha |
| `src/NotificationWorker/Extensions/OtelExtensions.cs` | Modificar — +1 linha |
| `grafana/provisioning/datasources/otel-poc-datasource-exemplars.yaml` | Criar (novo) |
| `docker-compose.yaml` | Modificar — +1 linha no `lgtm.volumes` |
| `otelcol.yaml` | Sem mudanças (verificação apenas) |

**Total de linhas de código alteradas:** ~5 linhas (trivial, alta confiança no resultado)
