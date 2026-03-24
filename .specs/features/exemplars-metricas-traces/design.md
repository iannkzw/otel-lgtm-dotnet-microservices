# Design — exemplars-metricas-traces

**Status:** Desenhado
**Milestone:** M5 — Correlação Observabilidade
**Criado em:** 2026-03-23

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ .NET OTel SDK                                                                  │
│                                                                                │
│  OrderService           ProcessingWorker        NotificationWorker            │
│  WithMetrics() +        WithMetrics() +          WithMetrics() +              │
│  ExemplarFilter         ExemplarFilter            ExemplarFilter               │
│  .AlwaysOn              .AlwaysOn                 .AlwaysOn                   │
│                                                                                │
│  orders.create.duration  orders.processing.duration  notifications.persistence.duration │
│       [traceId]               [traceId]                    [traceId]           │
└───────────────────────────────────┬─────────────────────────────────────────┘
                                    │ OTLP/gRPC (métricas + exemplars)
                                    ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│ OTel Collector  (otelcol.yaml)                                                 │
│                                                                                │
│  receivers: [otlp]                                                             │
│  processors: [memory_limiter, batch]   ← sem transform, sem drop de exemplars │
│  exporters: [otlphttp/metrics]                                                 │
│                                                                                │
│  Exemplars passam através do pipeline sem modificação (comportamento padrão)   │
└───────────────────────────────────┬─────────────────────────────────────────┘
                                    │ OTLP/HTTP → http://lgtm:4318/v1/metrics
                                    ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│ grafana/otel-lgtm (container lgtm)                                             │
│                                                                                │
│  Internal OTel Collector → Prometheus (local, :9090)                           │
│  Prometheus com suporte a exemplar storage (validar na image)                  │
│                                                                                │
│  Grafana datasources (via provisioning):                                       │
│    - prometheus  (UID: prometheus)  ← adicionar exemplarTraceIdDestinations    │
│    - tempo       (UID: tempo)       ← destino dos links de exemplar            │
└──────────────────────────────────────────────────────────────────────────────┘
```

---

## Componentes Afetados

| Arquivo | Mudança | Impacto |
|---|---|---|
| `src/OrderService/Extensions/OtelExtensions.cs` | `.SetExemplarFilter(ExemplarFilterType.AlwaysOn)` em `WithMetrics()` | OrderService emite exemplars |
| `src/ProcessingWorker/Extensions/OtelExtensions.cs` | Mesmo que acima | ProcessingWorker emite exemplars |
| `src/NotificationWorker/Extensions/OtelExtensions.cs` | Mesmo que acima | NotificationWorker emite exemplars |
| `grafana/provisioning/datasources/otel-poc-datasource-exemplars.yaml` | **Novo arquivo** — update do datasource Prometheus com `exemplarTraceIdDestinations` | Grafana exibe ícone de exemplar e gera link para Tempo |
| `docker-compose.yaml` | Novo bind mount do arquivo de datasource acima | Grafana carrega o override no boot |
| `otelcol.yaml` | **Sem mudança** — pipeline já passa exemplars por padrão | N/A |

---

## Decisões Técnicas

### DT-01: `AlwaysOn` vs `TraceBased` ExemplarFilter

**Escolha:** `ExemplarFilterType.AlwaysOn`

**Razão:** Em contexto de PoC, toda medição com trace context ativo deve gerar um exemplar. `TraceBased` respeitaria as decisões de sampling do `tail_sampling` no collector, mas como o filtro opera no SDK (antes de chegar ao collector), isso não faz diferença prática para os traces aceitos. `AlwaysOn` garante cobertura máxima de exemplars para demonstração.

**Trade-off:** Em produção com alto volume, `TraceBased` seria preferível para evitar overhead de metadados. Para a PoC, `AlwaysOn` é adequado.

### DT-02: Novo arquivo de datasource em vez de editar o datasource nativo do `otel-lgtm`

**Escolha:** Criar `grafana/provisioning/datasources/otel-poc-datasource-exemplars.yaml` e montá-lo em `/otel-lgtm/grafana/conf/provisioning/datasources/`

**Razão:** A imagem `grafana/otel-lgtm` provisiona seus datasources nativos de forma interna. Criar um arquivo separado no mesmo diretório de provisioning do Grafana é a abordagem idiomática: o Grafana aplica todos os arquivos YAML do diretório. Ao usar o mesmo `uid: prometheus` (UID padrão da imagem), o Grafana atualiza o datasource existente adicionando os campos de exemplar sem recriar nem conflitar.

**Trade-off:** Depende do UID padrão ser `prometheus`. Se a versão da imagem mudar o UID, o override não terá efeito. A validação deve confirmar o UID via `GET http://localhost:3000/api/datasources`.

### DT-03: Sem alterações no `otelcol.yaml`

**Razão:** O exporter `otlphttp/metrics` envia o payload OTLP completo, que inclui exemplars como campos nativos do protocolo de serialização. Os processors `memory_limiter` e `batch` não inspecionam nem filtram campos de exemplar. Nenhuma configuração adicional é necessária no collector para preservar exemplars.

---

## Código de Referência

### OtelExtensions.cs — adição do ExemplarFilter (igual nos 3 serviços)

```csharp
// Antes:
.WithMetrics(builder => builder
    .SetResourceBuilder(resourceBuilder)
    .AddMeter(OrderMetrics.MeterName)
    .AddOtlpExporter(options =>
    {
        options.Endpoint = new Uri(otlpEndpoint);
        options.Protocol = OtlpExportProtocol.Grpc;
    }))

// Depois:
.WithMetrics(builder => builder
    .SetResourceBuilder(resourceBuilder)
    .AddMeter(OrderMetrics.MeterName)
    .SetExemplarFilter(ExemplarFilterType.AlwaysOn)
    .AddOtlpExporter(options =>
    {
        options.Endpoint = new Uri(otlpEndpoint);
        options.Protocol = OtlpExportProtocol.Grpc;
    }))
```

> `ExemplarFilterType` está em `OpenTelemetry.Metrics` — sem new package NuGet (já referenciado via `Directory.Build.props`).

### grafana/provisioning/datasources/otel-poc-datasource-exemplars.yaml

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

> **Nota:** O `url` deve corresponder ao endpoint interno do Prometheus dentro do container `lgtm`. Inspecionar via `docker exec lgtm cat /otel-lgtm/grafana/conf/provisioning/datasources/*.yaml` para confirmar o URL exato se necessário.

### docker-compose.yaml — novo bind mount

```yaml
lgtm:
  volumes:
    # ... volumes existentes ...
    - ./grafana/provisioning/datasources/otel-poc-datasource-exemplars.yaml:/otel-lgtm/grafana/conf/provisioning/datasources/otel-poc-datasource-exemplars.yaml:ro
```

---

## Fluxo de Dados com Exemplars

```
1. POST /orders chega no OrderService
2. Span "CreateOrder" inicia → TraceId e SpanId ficam no ActivityContext
3. OrderMetrics.RecordCreateResult() chama _histogram.Record(duration, tags)
4. SDK OTel detecta span ativo → anexa {traceId, spanId} à medição como Exemplar
5. OtlpExporter serializa a medição com o Exemplar embutido no protobuf
6. OTel Collector recebe via gRPC, faz batch e envia via HTTP para lgtm:4318/v1/metrics
7. Prometheus interno do lgtm armazena o exemplar junto ao bucket do histograma
8. Grafana Explore/Dashboard executa PromQL → recebe série com exemplars
9. Grafana exibe ícone de exemplar no gráfico
10. Engenheiro clica → Grafana lê exemplar.traceID e abre Tempo com esse traceId
```

---

## Riscos e Mitigações

| Risco | Probabilidade | Mitigação |
|---|---|---|
| `otel-lgtm` não habilita `--enable-feature=exemplar-storage` no Prometheus interno | Média | Verificar via `docker exec lgtm ps aux` ou `docker exec lgtm cat /otel-lgtm/prometheus/prometheus.yml`; se ausente, será necessário montar uma config customizada de Prometheus |
| UID do datasource Prometheus difere de `"prometheus"` | Baixa | Confirmar via `GET http://localhost:3000/api/datasources` após subir a stack; ajustar o YAML se necessário |
| Exemplars não aparecem no Grafana mesmo com tudo configurado | Baixa | Verificar com `curl` direto na API do Prometheus: `GET http://localhost:9090/api/v1/query_exemplars?query=orders_create_duration_bucket` |
