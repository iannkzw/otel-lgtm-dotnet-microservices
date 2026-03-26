# 05 - OpenTelemetry

## Indice

1. [O que e OpenTelemetry](#1-o-que-e-opentelemetry)
2. [Arquitetura do OTel](#2-arquitetura-do-otel)
3. [Sinais](#3-sinais)
4. [Instrumentacao em .NET](#4-instrumentacao-em-net)
5. [OTel Collector](#5-otel-collector)
6. [Tail Sampling no Collector](#6-tail-sampling-no-collector)
7. [Exemplars](#7-exemplars)
8. [Referencias](#8-referencias)

---

## 1. O que e OpenTelemetry

OpenTelemetry (OTel) e um framework de observabilidade open source mantido pela CNCF (Cloud Native Computing Foundation). Ele nasceu da fusao de dois projetos anteriores:

- **OpenTracing** (2016): padrao de API para tracing distribuido
- **OpenCensus** (2018): biblioteca do Google para metricas e traces

Em 2019, os dois projetos se uniram para formar o OpenTelemetry, combinando o melhor de ambos. Hoje e o segundo projeto mais ativo da CNCF (atras apenas do Kubernetes).

### Proposta de valor

| Problema | Solucao OTel |
|----------|-------------|
| Vendor lock-in (Datadog, New Relic, etc) | API e SDK padronizados; troque de backend sem mudar codigo |
| Instrumentacao fragmentada | Um unico SDK para traces, metricas e logs |
| Falta de correlacao entre sinais | TraceId compartilhado entre traces, logs e metricas (exemplars) |
| Custo de instrumentacao manual | Auto-instrumentacao para frameworks populares |

---

## 2. Arquitetura do OTel

O OpenTelemetry e composto por tres camadas principais:

### 2.1 API

Interface estavel para instrumentacao. Codigo de aplicacao e bibliotecas instrumentam usando a API. Se nenhum SDK estiver registrado, as chamadas sao no-op (zero overhead).

### 2.2 SDK

Implementacao da API que adiciona:

- **Sampling**: decisao de quais traces coletar
- **Processing**: enriquecimento e transformacao de dados
- **Export**: envio para backends (OTLP, Jaeger, Zipkin, etc)

### 2.3 Collector

Componente independente que recebe, processa e exporta telemetria. Pode funcionar como:

- **Agent**: sidecar junto a aplicacao
- **Gateway**: servico centralizado que recebe de multiplas aplicacoes

### Diagrama simplificado

```
                        OTLP (gRPC)                  OTLP (HTTP)
  +-----------------+  port 4317    +-------------+  port 4318    +------------------+
  |                 | ------------> |             | ------------> |                  |
  |  App .NET       |               |  OTel       |               |  Backend         |
  |  (SDK embutido) |               |  Collector  |               |  (Grafana LGTM)  |
  |                 | ------------> |             | ------------> |                  |
  +-----------------+               +-------------+               +------------------+
     Traces, Metrics, Logs           Recebe, processa,             Armazena e
     gerados pelo SDK                filtra e reenvia              visualiza
```

Na PoC, o fluxo concreto e:

```
OrderService        --\
ProcessingWorker    ---}--> otelcol:4317 (gRPC) --> Collector --> lgtm:4318 (HTTP) --> Prometheus/Loki/Tempo
NotificationWorker  --/
```

---

## 3. Sinais

OpenTelemetry define quatro tipos de sinais de telemetria:

| Sinal | Descricao | Pergunta que responde |
|-------|-----------|----------------------|
| **Traces** | Caminho de uma requisicao atraves de servicos | "Onde esta a latencia? Qual servico falhou?" |
| **Metrics** | Medidas numericas agregadas ao longo do tempo | "Qual o throughput? Quantos erros por minuto?" |
| **Logs** | Registros textuais de eventos discretos | "O que aconteceu naquele momento especifico?" |
| **Baggage** | Contexto propagado entre servicos (key-value) | "Qual o tenant/regiao desta requisicao?" |

### Como os sinais se conectam

```
Metricas  ----exemplars----->  Traces  <-----traceId-----  Logs
   |                             |                           |
   |  "taxa de erro subiu"       |  "trace completo"         |  "detalhes do erro"
   v                             v                           v
  Grafana (PromQL)         Grafana (Tempo)            Grafana (Loki)
```

---

## 4. Instrumentacao em .NET

### 4.1 Auto-instrumentacao (bibliotecas)

O OTel .NET oferece bibliotecas de instrumentacao automatica para frameworks populares. Basta adicionar o pacote NuGet e registrar no SDK.

| Biblioteca | Pacote NuGet | O que captura | Servicos na PoC |
|------------|-------------|---------------|------------------|
| ASP.NET Core | `OpenTelemetry.Instrumentation.AspNetCore` | Requisicoes HTTP recebidas | OrderService |
| HttpClient | `OpenTelemetry.Instrumentation.Http` | Requisicoes HTTP enviadas | Todos |
| EF Core | `OpenTelemetry.Instrumentation.EntityFrameworkCore` | Queries SQL | OrderService, NotificationWorker |

### 4.2 Instrumentacao manual

Para logica de negocio customizada, use a API do .NET diretamente:

**Traces (spans) com ActivitySource:**

```csharp
// Declarar o ActivitySource (uma vez por classe/modulo)
private static readonly ActivitySource _activitySource = new("OrderService.Orders");

// Criar um span
using var activity = _activitySource.StartActivity("ProcessOrder");
activity?.SetTag("order.id", orderId);
activity?.SetTag("order.total", total);

// Em caso de erro
activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
activity?.RecordException(exception);
```

**Metricas com Meter:**

```csharp
// Declarar o Meter (uma vez)
private static readonly Meter _meter = new("OrderService.Metrics");

// Criar instrumentos
private static readonly Counter<long> _ordersCreated = _meter.CreateCounter<long>(
    "orders.created",
    description: "Total de pedidos criados");

private static readonly Histogram<double> _orderProcessingDuration = _meter.CreateHistogram<double>(
    "orders.processing.duration",
    unit: "ms",
    description: "Duracao do processamento de pedidos");

// Registrar valores
_ordersCreated.Add(1, new KeyValuePair<string, object?>("order.type", "standard"));

using var timer = new ValueStopwatch();
// ... logica ...
_orderProcessingDuration.Record(timer.GetElapsedTime().TotalMilliseconds);
```

### 4.3 Como a PoC faz: OtelExtensions.cs

Cada servico tem um `Extensions/OtelExtensions.cs` com o metodo `AddOtelInstrumentation()` que registra os tres sinais. Exemplo simplificado do `OrderService`:

```csharp
public static IServiceCollection AddOtelInstrumentation(
    this IServiceCollection services, IConfiguration configuration)
{
    var serviceName = configuration["OTEL_SERVICE_NAME"] ?? "order-service";
    var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
    var resourceBuilder = ResourceBuilder.CreateDefault()
        .AddService(serviceName, serviceVersion: serviceVersion);

    services
        .AddOpenTelemetry()
        // --- TRACES ---
        .WithTracing(builder => builder
            .SetResourceBuilder(resourceBuilder)
            .AddSource(ActivitySourceName)           // spans manuais
            .AddAspNetCoreInstrumentation()          // HTTP recebido
            .AddEntityFrameworkCoreInstrumentation()  // SQL
            .AddHttpClientInstrumentation()           // HTTP enviado
            .AddOtlpExporter(o => {
                o.Endpoint = new Uri(otlpEndpoint);
                o.Protocol = OtlpExportProtocol.Grpc;
            }))
        // --- METRICAS ---
        .WithMetrics(builder => builder
            .SetResourceBuilder(resourceBuilder)
            .AddMeter(OrderMetrics.MeterName)
            .SetExemplarFilter(ExemplarFilterType.AlwaysOn)
            .AddOtlpExporter(o => {
                o.Endpoint = new Uri(otlpEndpoint);
                o.Protocol = OtlpExportProtocol.Grpc;
            }));

    // --- LOGS ---
    services.AddLogging(logging =>
        logging.AddOpenTelemetry(options => {
            options.SetResourceBuilder(resourceBuilder);
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.AddOtlpExporter(e => {
                e.Endpoint = new Uri(otlpEndpoint);
                e.Protocol = OtlpExportProtocol.Grpc;
            });
        }));

    return services;
}
```

### Diferencas entre servicos

| Servico | ActivitySource | Instrumentacao auto | Meter |
|---------|---------------|---------------------|-------|
| OrderService | `OrderService.Orders` | ASP.NET Core + EF Core + HttpClient | `OrderMetrics` |
| ProcessingWorker | `ProcessingWorker.Worker` | HttpClient | `ProcessingMetrics` |
| NotificationWorker | `NotificationWorker.Worker` | EF Core + HttpClient | `NotificationMetrics` |

---

## 5. OTel Collector

O Collector e o componente central de infraestrutura de observabilidade. Ele desacopla as aplicacoes dos backends, permitindo processar, filtrar e rotear telemetria.

### 5.1 Receivers

Receivers definem como o Collector recebe dados.

Na PoC (`infra/otel/otelcol.yaml`):

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: "${POD_IP}:4317"
      http:
        endpoint: "${POD_IP}:4318"
  prometheus:
    config:
      scrape_configs:
        - job_name: otelcol
          scrape_interval: 60s
          static_configs:
            - targets:
                - ${POD_IP}:8888
```

| Receiver | Protocolo | Funcao |
|----------|-----------|--------|
| `otlp` (gRPC) | gRPC na porta 4317 | Recebe traces, metricas e logs dos servicos .NET |
| `otlp` (HTTP) | HTTP na porta 4318 | Alternativa HTTP para OTLP |
| `prometheus` | Scrape HTTP | Coleta metricas internas do proprio Collector |

### 5.2 Processors

Processors transformam dados entre receivers e exporters.

```yaml
processors:
  memory_limiter:
    check_interval: 30s
    limit_mib: 2024
    spike_limit_mib: 1500

  tail_sampling:
    decision_wait: 5s
    num_traces: 10000
    policies: ${file:processors/sampling/policies.yaml}

  span:
    exclude:
      match_type: strict
      span_names: ["Transaction.commit"]

  batch:
    send_batch_size: 1000
    timeout: 5s
```

| Processor | Funcao | Aplicado em |
|-----------|--------|-------------|
| `memory_limiter` | Previne OOM; descarta dados se memoria exceder limite | Traces, Logs, Metricas |
| `tail_sampling` | Amostragem inteligente de traces (ver secao 6) | Traces |
| `span` | Filtra spans indesejados (ex: `Transaction.commit`) | Traces |
| `batch` | Agrupa dados em lotes para exportacao eficiente | Traces, Metricas |

### 5.3 Exporters

Exporters enviam dados para backends:

```yaml
exporters:
  debug: {}
  otlphttp/traces:
    traces_endpoint: ${TRACES_URL}
  otlphttp/logs:
    logs_endpoint: ${LOGS_URL}
  otlphttp/metrics:
    metrics_endpoint: ${METRICS_URL}
```

| Exporter | Destino | Sinal |
|----------|---------|-------|
| `otlphttp/traces` | Tempo (via LGTM) | Traces |
| `otlphttp/logs` | Loki (via LGTM) | Logs |
| `otlphttp/metrics` | Prometheus (via LGTM) | Metricas |
| `debug` | Stdout do Collector | Todos (para debug) |

### 5.4 Pipelines

Pipelines conectam receivers, processors e exporters:

```yaml
service:
  pipelines:
    traces:
      receivers: [otlp]
      processors: [memory_limiter, tail_sampling, span, batch]
      exporters: [otlphttp/traces, debug]
    logs:
      receivers: [otlp]
      processors: [memory_limiter]
      exporters: [otlphttp/logs, debug]
    metrics:
      receivers: [otlp, prometheus]
      processors: [memory_limiter, batch]
      exporters: [otlphttp/metrics, debug]
```

**Observacoes sobre a configuracao da PoC:**

- **Traces** passam por 4 processors: protecao de memoria, amostragem, filtragem de spans, e batch
- **Logs** passam apenas por `memory_limiter` (sem amostragem nem batch)
- **Metricas** recebem de dois receivers: `otlp` (dos servicos) e `prometheus` (metricas internas do Collector)

---

## 6. Tail Sampling no Collector

### O que e

Tail sampling e uma estrategia de amostragem que toma a decisao de manter ou descartar um trace **apos** ele estar completo. Diferente do head sampling (decisao no inicio), o tail sampling tem acesso a todos os spans do trace e pode tomar decisoes inteligentes.

| Tipo | Quando decide | Vantagem | Desvantagem |
|------|--------------|----------|-------------|
| Head sampling | No inicio do trace | Baixo custo de memoria | Pode descartar traces com erro |
| **Tail sampling** | **Apos trace completo** | **Mantem 100% dos erros** | **Requer mais memoria no Collector** |

### Configuracao na PoC

O tail sampling esta configurado com `decision_wait: 5s` (espera 5 segundos para o trace completar) e `num_traces: 10000` (mantem ate 10.000 traces em memoria simultaneamente).

As policies sao carregadas de arquivos individuais:

#### Policy 1: `drop-health-checks` (AND)

```yaml
name: drop-health-checks
type: and
and:
  and_sub_policy:
    - name: health-url-paths
      type: string_attribute
      string_attribute:
        key: url.path
        values: ["^/health.*", "^/ready.*", "^/live.*", "^/actuator/health.*"]
        enabled_regex_matching: true

    - name: http-get-method
      type: string_attribute
      string_attribute:
        key: http.request.method
        values: ["GET"]

    - name: http-success-status
      type: numeric_attribute
      numeric_attribute:
        key: http.response.status_code
        min_value: 200
        max_value: 200

    - name: probabilistic-drop
      type: probabilistic
      probabilistic:
        sampling_percentage: 0
```

**Logica:** Se o path e de health check **E** o metodo e GET **E** o status e 200 **E** o sampling e 0% --> descarta o trace. Isso elimina traces de alta frequencia e baixo valor sem risco de perder health checks que falharam.

#### Policy 2: `keep-errors`

```yaml
name: keep-errors
type: status_code
status_code:
  status_codes:
    - ERROR
```

**Logica:** Mantem 100% dos traces com status `ERROR`. Traces de erro sao os mais valiosos para troubleshooting.

#### Policy 3: `sample-default`

```yaml
name: sample-everything-else
type: always_sample
```

**Logica:** Catch-all que mantem todos os traces restantes. Em producao, considere trocar por `probabilistic` com um percentual menor.

### Ordem de avaliacao

As policies sao avaliadas na ordem e a **primeira que faz match** decide. Logo:

1. Health checks bem-sucedidos --> **descartados**
2. Traces com erro --> **mantidos**
3. Todos os outros --> **mantidos** (always_sample)

### Como criar novas policies

Para adicionar uma nova policy, crie um arquivo YAML em `infra/otel/processors/sampling/` e adicione a referencia em `policies.yaml`:

```yaml
# infra/otel/processors/sampling/sample-high-latency.yaml
name: keep-high-latency
type: latency
latency:
  threshold_ms: 5000   # mantem traces com duracao > 5s
```

```yaml
# Em policies.yaml, adicionar antes do sample-default:
- ${file:processors/sampling/sample-high-latency.yaml}
```

---

## 7. Exemplars

### O que sao

Exemplars sao uma ponte entre metricas e traces. Sao amostras individuais anexadas a pontos de metricas que carregam o `trace_id` da requisicao que gerou aquele valor.

Exemplo: um histogram de latencia mostra que o p99 e 500ms. O exemplar aponta para o trace exato de uma requisicao que levou 500ms, permitindo investigar por que.

### Como habilitar na PoC

Em cada servico, na configuracao de metricas:

```csharp
.WithMetrics(builder => builder
    .SetResourceBuilder(resourceBuilder)
    .AddMeter(OrderMetrics.MeterName)
    .SetExemplarFilter(ExemplarFilterType.AlwaysOn)  // <-- habilita exemplars
    .AddOtlpExporter(options => { ... }))
```

| Valor do ExemplarFilter | Comportamento |
|------------------------|---------------|
| `AlwaysOff` | Nunca coleta exemplars (padrao) |
| `TraceBased` | Coleta apenas quando o trace esta sendo amostrado |
| **`AlwaysOn`** | **Coleta sempre (usado na PoC)** |

### Fluxo completo

```
1. App registra metrica          -->  histogram.Record(latency)
2. SDK anexa exemplar            -->  {trace_id: "abc123", span_id: "def456", value: 487.3}
3. OTLP exporta para Collector   -->  metrica + exemplar enviados juntos
4. Collector repassa ao backend  -->  Prometheus armazena metrica + exemplar
5. Grafana exibe no dashboard    -->  pontos clicaveis no grafico de metricas
6. Usuario clica no exemplar     -->  Grafana abre o trace no Tempo
```

### Como visualizar no Grafana

1. Abra um painel de metricas (ex: histogram de latencia)
2. Exemplars aparecem como **pontos** sobrepostos no grafico
3. Ao passar o mouse, veja o `trace_id` e o valor
4. Clique para navegar diretamente ao trace no Tempo

Para mais detalhes sobre dashboards e visualizacao, consulte o **doc 07**.

---

## 8. Referencias

| Recurso | Link |
|---------|------|
| OpenTelemetry - Site oficial | https://opentelemetry.io/ |
| OpenTelemetry - Specification | https://opentelemetry.io/docs/specs/otel/ |
| OTel .NET SDK | https://opentelemetry.io/docs/languages/dotnet/ |
| OTel Collector | https://opentelemetry.io/docs/collector/ |
| OTel Collector - Tail Sampling | https://github.com/open-telemetry/opentelemetry-collector-contrib/tree/main/processor/tailsamplingprocessor |
| Exemplars - Specification | https://opentelemetry.io/docs/specs/otel/metrics/data-model/#exemplars |
| CNCF - OpenTelemetry | https://www.cncf.io/projects/opentelemetry/ |
| .NET ActivitySource API | https://learn.microsoft.com/dotnet/api/system.diagnostics.activitysource |
| .NET Meter API | https://learn.microsoft.com/dotnet/api/system.diagnostics.metrics.meter |
