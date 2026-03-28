# Distributed Tracing

## Sumario

1. [O que e Distributed Tracing](#o-que-e-distributed-tracing)
2. [Conceitos Fundamentais](#conceitos-fundamentais)
3. [W3C TraceContext](#w3c-tracecontext)
4. [Propagacao em Sistemas Assincronos](#propagacao-em-sistemas-assincronos)
5. [Sampling](#sampling)
6. [Analisando Traces no Tempo](#analisando-traces-no-tempo)
7. [Avancado](#avancado)
8. [Referencias](#referencias)

---

## O que e Distributed Tracing

Distributed tracing e a tecnica de **rastrear uma requisicao enquanto ela atravessa multiplos servicos** em uma arquitetura distribuida. Cada servico registra sua parte do trabalho (um "span"), e esses spans sao conectados formando um grafo que representa o caminho completo da requisicao.

### O problema que resolve

Em um sistema monolitico, um stack trace mostra toda a cadeia de chamadas. Em microservicos, a requisicao cruza fronteiras de processo, rede e ate protocolos de comunicacao. Sem tracing distribuido, voce tem logs isolados em cada servico, sem como conecta-los.

**Analogia:** Imagine uma encomenda internacional que passa por varios centros de distribuicao. Cada centro carimba a encomenda com a data, hora e operacao realizada. No final, voce pode reconstruir todo o trajeto lendo os carimbos na ordem. Distributed tracing faz o mesmo para requisicoes em microservicos -- cada servico "carimba" (cria um span) sua participacao.

### Fluxo na PoC

```
POST /orders (OrderService)
  |-- Persiste Order + OutboxMessage (EF Core, span automatico)
  |-- Debezium CDC extrai do outbox e publica no Kafka
       |-- traceparent/tracestate preservados como Kafka headers
       |
       v
  Kafka topic "orders"
       |
       v
  ProcessingWorker (consome, extrai contexto, cria span filho)
  |-- GET /orders/{id} (HttpClient, span automatico)
  |-- Publica no Kafka topic "notifications"
       |-- traceparent/tracestate injetados nos headers
       |
       v
  Kafka topic "notifications"
       |
       v
  NotificationWorker (consome, extrai contexto, cria span filho)
  |-- Persiste notificacao (EF Core, span automatico)
```

Todos os spans compartilham o mesmo `trace_id`, formando um unico trace end-to-end.

---

## Conceitos Fundamentais

### Trace

Um **trace** representa a jornada completa de uma requisicao pelo sistema. E identificado por um `trace_id` unico (128 bits, representado como 32 caracteres hexadecimais).

```
trace_id: 4bf92f3577b34da6a3ce929d0e0e4736
```

Um trace e composto por um ou mais **spans** organizados em uma arvore (relacao pai-filho).

### Span

Um **span** representa uma **unidade de trabalho** dentro de um trace. Cada span contem:

| Campo | Descricao | Exemplo |
|-------|-----------|---------|
| `trace_id` | ID do trace ao qual pertence | `4bf92f3577b34da6a3ce929d0e0e4736` |
| `span_id` | ID unico deste span (64 bits, 16 hex) | `00f067aa0ba902b7` |
| `parent_span_id` | ID do span pai (vazio se for o root span) | `a3ce929d0e0e4736` |
| `name` | Nome descritivo da operacao | `POST /orders`, `orders consume` |
| `start_time` | Timestamp de inicio | `2025-01-15T10:30:00.123Z` |
| `end_time` | Timestamp de fim | `2025-01-15T10:30:00.456Z` |
| `status` | OK, ERROR ou UNSET | `ERROR` |
| `attributes` | Pares chave-valor com metadados | `http.method=POST`, `db.system=postgresql` |
| `events` | Logs vinculados ao span (com timestamp) | Exception com stack trace |
| `kind` | Tipo do span | `SERVER`, `CLIENT`, `PRODUCER`, `CONSUMER` |

#### Relacao pai-filho

```
[Root Span: POST /orders]           (span_id=A, parent=none)
  |-- [EF Core: INSERT orders]      (span_id=B, parent=A)
  |-- [EF Core: INSERT outbox]      (span_id=C, parent=A)

[Span: orders consume]              (span_id=D, parent=A, via link)
  |-- [HttpClient: GET /orders/1]   (span_id=E, parent=D)
  |-- [Kafka produce: notifications](span_id=F, parent=D)

[Span: notifications consume]       (span_id=G, parent=F, via link)
  |-- [EF Core: INSERT notification](span_id=H, parent=G)
```

### Context Propagation

Context propagation e o mecanismo que **transmite o trace_id e o span_id entre servicos**, permitindo que spans criados em servicos diferentes sejam conectados no mesmo trace.

Sem propagacao de contexto, cada servico criaria traces independentes e seria impossivel reconstruir o fluxo completo.

Na PoC, a propagacao acontece em dois cenarios:

| Cenario | Mecanismo |
|---------|-----------|
| HTTP (sincrono) | Header `traceparent` automaticamente injetado/extraido pelo ASP.NET Core e HttpClient |
| Kafka (assincrono) | Headers `traceparent`/`tracestate` injetados/extraidos manualmente via `W3CTraceContext` e `KafkaTracingHelper` |

---

## W3C TraceContext

O [W3C Trace Context](https://www.w3.org/TR/trace-context/) e o **padrao aberto** para propagacao de contexto. E o formato padrao do OpenTelemetry e do .NET.

### Header `traceparent`

Formato: `{version}-{trace_id}-{parent_id}-{trace_flags}`

```
traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
              |  |                                |                |
              |  |                                |                +-- trace_flags (01 = sampled)
              |  |                                +-- parent_id (span_id do chamador)
              |  +-- trace_id (128 bits)
              +-- version (sempre "00" atualmente)
```

| Campo | Tamanho | Descricao |
|-------|---------|-----------|
| `version` | 2 hex (1 byte) | Versao do formato. Atualmente sempre `00` |
| `trace_id` | 32 hex (16 bytes) | Identificador unico do trace completo |
| `parent_id` | 16 hex (8 bytes) | `span_id` do span que originou a chamada |
| `trace_flags` | 2 hex (1 byte) | Flags de controle. `01` = sampled (trace sera gravado) |

### Header `tracestate`

O `tracestate` carrega **informacoes especificas de vendors** (como Datadog, Dynatrace, etc.) sem interferir no `traceparent`. Formato de lista chave-valor separada por virgulas:

```
tracestate: vendor1=valor1,vendor2=valor2
```

A PoC propaga o `tracestate` para compatibilidade, mas nao adiciona dados customizados a ele.

### Como a PoC usa W3C TraceContext

O fluxo completo de propagacao:

**1. OrderService grava contexto no Outbox:**

A entidade `OutboxMessage` possui campos `Traceparent` e `Tracestate`. Quando o OrderService cria um pedido, grava o contexto do trace ativo nesses campos:

```csharp
// OutboxMessage.cs (campos relevantes)
public string? Traceparent { get; set; }
public string? Tracestate { get; set; }
```

**2. Debezium extrai e publica como headers Kafka:**

O Debezium CDC le a tabela outbox e publica no Kafka. Os campos `traceparent` e `tracestate` da tabela sao mapeados como headers da mensagem Kafka via a configuracao do SMT (Single Message Transform) do Debezium.

**3. Workers extraem e criam spans filhos:**

Os workers utilizam `KafkaTracingHelper.Extract()` para ler os headers e restaurar o contexto:

```csharp
// KafkaTracingHelper.cs
public static ActivityContext? Extract(Headers? headers)
{
    var traceParent = GetHeader(headers, "traceparent");
    var traceState = GetHeader(headers, "tracestate");
    return W3CTraceContext.Extract(traceParent, traceState);
}
```

O contexto extraido e usado como parent ao criar novos spans, mantendo a cadeia do trace:

```csharp
// Uso no Worker (simplificado)
var parentContext = KafkaTracingHelper.Extract(message.Headers);
using var activity = ActivitySource.StartActivity(
    "process order",
    ActivityKind.Consumer,
    parentContext ?? default);
```

**4. ProcessingWorker injeta contexto ao publicar:**

Ao publicar no topico "notifications", o ProcessingWorker injeta o contexto do span atual:

```csharp
// KafkaTracingHelper.cs
public static void Inject(Activity? activity, Headers headers)
{
    W3CTraceContext.Inject(activity, (key, value) =>
        SetHeader(headers, key, value));
}
```

---

## Propagacao em Sistemas Assincronos

### O desafio do Kafka

Em chamadas HTTP sincronas, o ASP.NET Core e o HttpClient automaticamente injetam e extraem headers de contexto. Com Kafka, isso **nao acontece automaticamente** porque:

1. O **produtor e o consumidor nao estao na mesma requisicao** -- a mensagem pode ser consumida minutos ou horas depois.
2. **Nao ha instrumentacao automatica** do Kafka no .NET que lide com propagacao W3C (ao contrario do HTTP).
3. No caso da PoC, ha um intermediario adicional: o **Debezium**, que le do banco e publica no Kafka, adicionando mais uma camada de indirecta.

### A solucao da PoC

A PoC resolve o problema com duas classes utilitarias:

**`W3CTraceContext` (Shared):** Abstrai a logica de serializar/deserializar o contexto W3C usando a API `System.Diagnostics.ActivityContext`:

```csharp
// Shared/W3CTraceContext.cs
public static class W3CTraceContext
{
    public static ActivityContext? Extract(string? traceParent, string? traceState)
    {
        if (string.IsNullOrWhiteSpace(traceParent))
            return null;
        return ActivityContext.TryParse(traceParent, traceState, out var ctx)
            ? ctx : null;
    }

    public static void Inject(Activity? activity, Action<string, string> setHeader)
    {
        if (activity?.Id is null or "") return;
        setHeader("traceparent", activity.Id);
        if (!string.IsNullOrWhiteSpace(activity.TraceStateString))
            setHeader("tracestate", activity.TraceStateString);
    }
}
```

**`KafkaTracingHelper` (cada Worker):** Adapta a interface generica do `W3CTraceContext` para os headers especificos do Kafka (`Confluent.Kafka.Headers`):

```csharp
// ProcessingWorker/Messaging/KafkaTracingHelper.cs
public static class KafkaTracingHelper
{
    public static void Inject(Activity? activity, Headers headers)
    {
        W3CTraceContext.Inject(activity, (key, value) =>
            SetHeader(headers, key, value));
    }

    public static ActivityContext? Extract(Headers? headers)
    {
        var traceParent = GetHeader(headers, "traceparent");
        var traceState = GetHeader(headers, "tracestate");
        return W3CTraceContext.Extract(traceParent, traceState);
    }
}
```

### Fluxo completo passo a passo

```
1. OrderService: Activity.Current.Id = "00-abc123...-def456...-01"
   -> Grava Traceparent="00-abc123...-def456...-01" no OutboxMessage
   -> Persiste no PostgreSQL

2. Debezium CDC: Le a linha do outbox
   -> Mapeia coluna "traceparent" para header Kafka "traceparent"
   -> Publica no topico "orders"

3. ProcessingWorker: Consome mensagem do Kafka
   -> KafkaTracingHelper.Extract(headers) retorna ActivityContext
   -> Cria novo span com parentContext = contexto extraido
   -> Span herda o trace_id "abc123..."
   -> Faz GET /orders/{id} (HttpClient propaga automaticamente)
   -> KafkaTracingHelper.Inject(Activity.Current, headers)
   -> Publica no topico "notifications"

4. NotificationWorker: Consome mensagem do Kafka
   -> KafkaTracingHelper.Extract(headers) retorna ActivityContext
   -> Cria novo span com parentContext = contexto extraido
   -> Span herda o mesmo trace_id "abc123..."
   -> Persiste notificacao (EF Core cria span automaticamente)
```

Resultado: **um unico trace conecta todos os servicos**, mesmo com comunicacao assincrona via Kafka e CDC.

---

## Sampling

Nem todo trace precisa ser armazenado. Em sistemas de alto throughput, armazenar 100% dos traces seria proibitivo em termos de custo e performance. Sampling decide **quais traces manter**.

### Head Sampling vs Tail Sampling

| Aspecto | Head Sampling | Tail Sampling |
|---------|--------------|---------------|
| **Quando decide** | No inicio do trace (primeiro span) | Apos o trace estar completo |
| **Baseado em** | Probabilidade ou atributos iniciais | Conteudo completo do trace (duracao, erros, atributos) |
| **Vantagem** | Simples, baixo overhead, nao requer buffering | Decisoes inteligentes: manter erros, drops longos, etc. |
| **Desvantagem** | Pode descartar traces com erros ou anomalias | Requer buffer de traces em memoria, mais complexo |
| **Onde executa** | No SDK (dentro do servico) | No Collector (componente centralizado) |
| **Ideal para** | Alta escala onde custo e prioridade e erros sao raros | Quando voce precisa garantir que erros e anomalias sejam sempre capturados |

### Tail Sampling na PoC

A PoC utiliza **tail sampling no OTel Collector** com tres politicas avaliadas em sequencia:

**Configuracao do Collector (`infra/otel/otelcol.yaml`):**

```yaml
processors:
  tail_sampling:
    decision_wait: 5s      # Tempo maximo para aguardar spans do mesmo trace
    num_traces: 10000       # Numero maximo de traces em buffer
    policies: ${file:processors/sampling/policies.yaml}
```

**Politicas (em ordem de avaliacao):**

| Politica | Tipo | Comportamento |
|----------|------|---------------|
| `drop-health-checks` | `and` (composta) | Descarta traces de health checks: `GET /health*`, `GET /ready*`, `GET /live*` com status 200. Usa `probabilistic` com 0% para descartar. |
| `keep-errors` | `status_code` | Mantem **todos** os traces que contem pelo menos um span com status `ERROR`. Garante que nenhum erro seja perdido. |
| `sample-everything-else` | `always_sample` | Mantem todos os traces restantes. Em producao, seria substituido por `probabilistic` (ex: 10%). |

**Detalhamento da politica `drop-health-checks`:**

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
        sampling_percentage: 0  # 0% = descarta tudo que matchou
```

### Quando usar cada abordagem

| Cenario | Recomendacao |
|---------|-------------|
| Desenvolvimento e PoC | `always_sample` (manter tudo) |
| Producao com baixo volume (< 1000 req/s) | Tail sampling: keep errors + probabilistic 50-100% |
| Producao com alto volume (> 10000 req/s) | Head sampling no SDK (10-20%) + tail sampling no Collector para erros |
| Health checks, readiness probes | Sempre descartar (alto volume, zero valor diagnostico) |

---

## Analisando Traces no Tempo

O Grafana Tempo e o backend de traces da stack LGTM. Aqui esta como usar traces para diagnosticar problemas.

### Buscar por trace_id

No Grafana, navegue ate Explore > Tempo. Existem duas formas de buscar:

**1. Busca direta por ID:**

Cole o `trace_id` no campo de busca. Util quando voce ja tem o ID (de um log, exemplar de metrica ou alerta).

**2. Via Exemplars (metricas -> traces):**

Em um painel de metricas no Grafana, exemplars aparecem como pontos clicaveis. Ao clicar, o Grafana navega diretamente para o trace no Tempo.

**3. Via Logs (Loki -> Tempo):**

Logs estruturados incluem `TraceId`. No Grafana, com a configuracao de datasource de exemplars, voce pode clicar no `TraceId` de um log e abrir o trace no Tempo.

### Identificar gargalos (spans longos)

No visualizador de traces do Grafana (waterfall view):

1. Observe o **comprimento relativo** dos spans. Spans longos sao potenciais gargalos.
2. Compare o tempo total do trace com a soma dos spans filhos. Se o span pai dura 500ms mas os filhos somam 100ms, os 400ms restantes sao **tempo de processamento do proprio servico** (CPU, GC, espera de I/O nao instrumentado).
3. Preste atencao em **lacunas entre spans** -- indicam tempo gasto em codigo nao instrumentado.

Exemplo de investigacao na PoC:

```
[POST /orders: 450ms]
  |-- [EF Core INSERT orders: 15ms]
  |-- [EF Core INSERT outbox: 10ms]
  |-- [??? 425ms nao explicados]  <-- gargalo: validacao custosa? serialization?
```

### Identificar erros (span status ERROR)

No Grafana Tempo:

1. Spans com status `ERROR` aparecem destacados (geralmente em vermelho).
2. Clique no span para ver os **attributes** e **events**.
3. Events de exception contem `exception.type`, `exception.message` e `exception.stacktrace`.

Exemplo:

```
Span: "GET /orders/123" (ProcessingWorker -> OrderService)
Status: ERROR
Attributes:
  http.response.status_code = 404
  http.request.method = GET
Events:
  - name: "exception"
    exception.type: "HttpRequestException"
    exception.message: "Order not found"
```

Isso indica que o ProcessingWorker tentou enriquecer um pedido que nao existe, provavelmente porque o pedido foi deletado entre a publicacao e o processamento.

---

## Avancado

### Span Links

Span links conectam spans que sao **relacionados mas nao tem relacao direta pai-filho**. Diferente de `parent_span_id`, um link e uma referencia fraca -- o span nao "pertence" ao trace do link.

**Quando usar:**

- Batch processing: um span de processamento em lote pode ter links para cada item individual.
- Fan-in: um span que consolida resultados de multiplos traces.
- Re-enqueueing: quando uma mensagem e reenfileirada para retry, o novo span pode ter um link para o span original.

```csharp
// Exemplo conceitual (nao implementado na PoC)
var link = new ActivityLink(originalContext);
using var activity = source.StartActivity(
    "retry process",
    ActivityKind.Consumer,
    parentContext: default,
    links: [link]);
```

### Baggage

Baggage sao pares chave-valor que viajam junto com o contexto de propagacao, acessiveis por todos os servicos no trace. Diferente de span attributes (que sao locais ao span), baggage e **propagado** automaticamente.

**Casos de uso:**

- Propagar `tenant_id` ou `region` sem que cada servico precise buscar essa informacao.
- Feature flags que afetam o comportamento downstream.

**Cuidado:** Baggage e transmitido em **todos** os requests downstream, adicionando overhead de rede. Use com parcimonia e nunca para dados sensiveis.

```csharp
// Exemplo conceitual
Baggage.SetBaggage("tenant.id", "acme-corp");

// Em qualquer servico downstream:
var tenantId = Baggage.GetBaggage("tenant.id"); // "acme-corp"
```

### Custom Attributes

Attributes customizados adicionam contexto de negocio aos spans, facilitando buscas e analises:

```csharp
// Exemplo na PoC (simplificado)
activity?.SetTag("order.id", orderId.ToString());
activity?.SetTag("order.total", order.TotalAmount);
activity?.SetTag("order.items_count", order.Items.Count);
```

**Boas praticas para attributes:**

| Pratica | Exemplo bom | Exemplo ruim |
|---------|-------------|--------------|
| Use namespaces | `order.id`, `customer.tier` | `id`, `tier` |
| Valores com cardinalidade controlada | `customer.tier="premium"` | `customer.email="user@x.com"` |
| Siga as convencoes semanticas OTel | `http.request.method`, `db.system` | `method`, `database` |
| Nao duplique informacao automatica | (ASP.NET ja grava `http.route`) | `activity.SetTag("route", ...)` |

---

## Referencias

### Especificacoes

- **W3C Trace Context:** [https://www.w3.org/TR/trace-context/](https://www.w3.org/TR/trace-context/)
- **W3C Trace Context Level 2:** [https://www.w3.org/TR/trace-context-2/](https://www.w3.org/TR/trace-context-2/)
- **OpenTelemetry Tracing Specification:** [https://opentelemetry.io/docs/specs/otel/trace/](https://opentelemetry.io/docs/specs/otel/trace/)
- **OpenTelemetry Semantic Conventions (Traces):** [https://opentelemetry.io/docs/specs/semconv/general/trace/](https://opentelemetry.io/docs/specs/semconv/general/trace/)

### Documentacao de Ferramentas

- **Grafana Tempo:** [https://grafana.com/docs/tempo/latest/](https://grafana.com/docs/tempo/latest/)
- **OTel Collector -- Tail Sampling Processor:** [https://github.com/open-telemetry/opentelemetry-collector-contrib/tree/main/processor/tailsamplingprocessor](https://github.com/open-telemetry/opentelemetry-collector-contrib/tree/main/processor/tailsamplingprocessor)
- **OpenTelemetry .NET -- Distributed Tracing:** [https://opentelemetry.io/docs/languages/dotnet/instrumentation/#traces](https://opentelemetry.io/docs/languages/dotnet/instrumentation/#traces)

### Artigos

- **Jaeger -- Introduction to Distributed Tracing:** [https://www.jaegertracing.io/docs/latest/architecture/](https://www.jaegertracing.io/docs/latest/architecture/)
- **Lightstep -- Distributed Tracing:** [https://opentelemetry.io/docs/concepts/signals/traces/](https://opentelemetry.io/docs/concepts/signals/traces/)
- **Google SRE Book -- Monitoring Distributed Systems:** [https://sre.google/sre-book/monitoring-distributed-systems/](https://sre.google/sre-book/monitoring-distributed-systems/)
