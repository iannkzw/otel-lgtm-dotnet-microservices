# Metricas

## Sumario

1. [O que sao Metricas](#o-que-sao-metricas)
2. [Tipos de Metricas](#tipos-de-metricas)
3. [Nomenclatura Prometheus](#nomenclatura-prometheus)
4. [Metricas da PoC](#metricas-da-poc)
5. [PromQL Essencial](#promql-essencial)
6. [Boas Praticas](#boas-praticas)
7. [Avancado](#avancado)
8. [Referencias](#referencias)

---

## O que sao Metricas

Metricas sao **valores numericos medidos ao longo do tempo**. Cada metrica e armazenada como uma **time series** -- uma sequencia de pares (timestamp, valor) identificada por um nome e um conjunto de labels (chave-valor).

```
orders_created_total{result="created", service="OrderService"} 42  @1711324800
orders_created_total{result="created", service="OrderService"} 43  @1711324815
```

### Caracteristicas fundamentais

| Propriedade | Descricao |
|------------|-----------|
| **Baixo custo** | Metricas sao valores numericos agregados; nao armazenam detalhes individuais como traces ou logs |
| **Alta cardinalidade temporal** | Coletadas a cada N segundos (tipicamente 15s), permitem analise detalhada ao longo do tempo |
| **Ideais para alertas** | Queries rapidas e previsaveis, adequadas para avaliacao continua por regras de alerta |
| **Dimensionais** | Labels permitem filtrar e agrupar (ex: por servico, resultado, topico Kafka) |

---

## Tipos de Metricas

### Tabela Comparativa

| Tipo | Comportamento | Quando usar | Operacao PromQL tipica |
|------|--------------|-------------|----------------------|
| **Counter** | So incrementa (ou reseta em restart) | Contar eventos: requisicoes, erros, mensagens | `rate()`, `increase()` |
| **Gauge** | Sobe e desce livremente | Valores instantaneos: temperatura, fila, conexoes ativas | Valor direto, `avg_over_time()` |
| **Histogram** | Distribui observacoes em buckets + soma + contagem | Medir duracoes e tamanhos com percentis | `histogram_quantile()` |
| **Summary** | Calcula quantis no cliente | Quando voce precisa de quantis exatos sem agregacao | Valor direto dos quantis |

---

### Counter

Um counter e um valor que **so cresce** (ou reseta para zero quando o processo reinicia). Nunca faz sentido olhar o valor absoluto de um counter; o que importa e a **taxa de variacao**.

**Exemplo na PoC:**

```csharp
// OrderService/Metrics/OrderMetrics.cs
_ordersCreatedCounter = _meter.CreateCounter<long>(
    "orders.created.total", unit: "{order}");

// Incremento com label "result"
_ordersCreatedCounter.Add(1, new("result", "created"));
```

**PromQL:**

```promql
# Taxa de pedidos criados por segundo (ultimos 5 minutos)
rate(orders_created_total{result="created"}[5m])

# Total de pedidos criados na ultima hora
increase(orders_created_total[1h])
```

**Regra:** Nunca use um counter sem `rate()` ou `increase()`. O valor absoluto nao tem significado pratico (muda a cada restart).

---

### Gauge

Um gauge e um valor que **sobe e desce** livremente. Representa o estado *atual* de algo.

**Exemplo na PoC:**

```csharp
// OrderService/Metrics/OrderMetrics.cs
_ordersBacklogGauge = _meter.CreateObservableGauge<long>(
    "orders.backlog.current", ObserveBacklog, unit: "{order}");

// O callback retorna medicoes com label "status"
new Measurement<long>(snapshot.PendingPublishCount,
    new KeyValuePair<string, object?>("status", "PendingPublish"));
```

```csharp
// ProcessingWorker/Metrics/ProcessingMetrics.cs
_kafkaConsumerLagGauge = _meter.CreateObservableGauge<long>(
    "kafka.consumer.lag", ObserveLag, unit: "{message}");

// Labels: topic, consumer_group
new Measurement<long>(snapshot.Lag,
    new("topic", snapshot.Topic),
    new("consumer_group", snapshot.ConsumerGroup));
```

**PromQL:**

```promql
# Backlog atual de pedidos pendentes
orders_backlog_current{status="PendingPublish"}

# Lag do consumer Kafka no ProcessingWorker
kafka_consumer_lag{consumer_group="processing-worker"}

# Media do lag nos ultimos 10 minutos
avg_over_time(kafka_consumer_lag{consumer_group="processing-worker"}[10m])
```

---

### Histogram

Um histogram distribui observacoes em **buckets** (faixas de valores). O Prometheus armazena tres time series para cada histogram:

- `_bucket{le="X"}` -- contagem acumulada de observacoes <= X
- `_sum` -- soma de todos os valores observados
- `_count` -- total de observacoes

**Exemplo na PoC:**

```csharp
// OrderService/Metrics/OrderMetrics.cs
_ordersCreateDurationHistogram = _meter.CreateHistogram<double>(
    "orders.create.duration", unit: "ms");

// Registro com label "result"
_ordersCreateDurationHistogram.Record(
    duration.TotalMilliseconds,
    new("result", "created"));
```

**PromQL:**

```promql
# P95 de latencia de criacao de pedidos (ms)
histogram_quantile(0.95,
  sum by (le) (
    rate(orders_create_duration_milliseconds_bucket{result="created"}[5m])
  )
)

# P99 de latencia de processamento
histogram_quantile(0.99,
  sum by (le) (
    rate(orders_processing_duration_milliseconds_bucket{result="processed"}[5m])
  )
)

# Latencia media de criacao de pedidos (ms)
rate(orders_create_duration_milliseconds_sum{result="created"}[5m])
/
rate(orders_create_duration_milliseconds_count{result="created"}[5m])
```

**Buckets:** Os buckets padrao do OpenTelemetry sao exponenciais. Para latencia HTTP, valores comuns sao: 5, 10, 25, 50, 75, 100, 250, 500, 750, 1000, 2500, 5000, 7500, 10000 ms. Buckets customizados podem ser configurados via Views no SDK.

---

### Summary

Um summary calcula **quantis diretamente no cliente** (ex: P50, P95, P99) em uma janela de tempo deslizante.

| Aspecto | Histogram | Summary |
|---------|-----------|---------|
| Calculo do quantil | No servidor (PromQL) | No cliente (SDK) |
| Agregacao entre instancias | Possivel (sum by) | **Impossivel** (quantis nao sao aditivios) |
| Custo no cliente | Baixo (incrementar buckets) | Alto (manter janela deslizante) |
| Precisao | Aproximada (depende dos buckets) | Exata para a instancia |

**Quando usar cada um:**

- **Histogram:** Na grande maioria dos casos. Permite agregacao, e o padrao do OpenTelemetry e do Prometheus.
- **Summary:** Quando voce precisa de quantis exatos de uma unica instancia e nao precisa agregar entre replicas. Caso raro em ambientes com multiplas replicas.

A PoC usa **exclusivamente Histograms**, que e a recomendacao padrao.

---

## Nomenclatura Prometheus

O OpenTelemetry SDK converte automaticamente os nomes das metricas para o formato Prometheus. Entender a convencao ajuda a escrever PromQL.

### Convencoes de nomes

| Regra | Exemplo OTel | Resultado Prometheus |
|-------|-------------|---------------------|
| Pontos viram underscores | `orders.created.total` | `orders_created_total` |
| Counters recebem sufixo `_total` | `orders.created` | `orders_created_total` |
| Unidade vira sufixo | `orders.create.duration` (unit: ms) | `orders_create_duration_milliseconds` |
| Histograms geram `_bucket`, `_sum`, `_count` | `orders.create.duration` | `orders_create_duration_milliseconds_bucket` |
| Gauges sem sufixo especial | `orders.backlog.current` | `orders_backlog_current` |

### Boas praticas de nomenclatura

- Use `snake_case` com pontos (OTel) ou underscores (Prometheus).
- Inclua a **unidade** no nome ou na metadata: `_seconds`, `_bytes`, `_total`.
- Use um **prefixo** do servico/dominio: `orders.`, `notifications.`, `kafka.`.
- Evite prefixos genericos como `app_` ou `service_`.

---

## Metricas da PoC

Tabela completa de todas as metricas instrumentadas na PoC:

| Metrica (OTel) | Tipo | Labels | Servico | Descricao |
|----------------|------|--------|---------|-----------|
| `orders.created.total` | Counter | `result`: created, validation_failed, persist_failed | OrderService | Total de tentativas de criacao de pedidos |
| `orders.create.duration` | Histogram (ms) | `result`: created, validation_failed, persist_failed | OrderService | Duracao da operacao de criacao do pedido (inclui persistencia no DB + outbox) |
| `orders.backlog.current` | Gauge | `status`: PendingPublish, PublishFailed | OrderService | Quantidade atual de mensagens no outbox aguardando publicacao pelo Debezium |
| `orders.processed.total` | Counter | `result`: processed, invalid_payload, not_found, http_error, timeout, network_error, publish_failed, unexpected_error | ProcessingWorker | Total de mensagens processadas do topico "orders" |
| `orders.processing.duration` | Histogram (ms) | `result`: (mesmos valores acima) | ProcessingWorker | Duracao do processamento completo (consume + enriquecimento HTTP + publish) |
| `kafka.consumer.lag` | Gauge | `topic`, `consumer_group` | ProcessingWorker, NotificationWorker | Diferenca entre o offset mais recente do topico e o offset do consumer (mensagens nao consumidas) |
| `notifications.persisted.total` | Counter | `result`: persisted, invalid_payload, persistence_failed, consume_failed, unexpected_error | NotificationWorker | Total de notificacoes processadas |
| `notifications.persistence.duration` | Histogram (ms) | `result`: persisted, persistence_failed | NotificationWorker | Duracao da persistencia da notificacao no banco de dados |

### Observacoes sobre as labels

- **`result`** e a label principal em quase todas as metricas. Ela distingue sucesso de diferentes tipos de falha, permitindo calcular taxa de erro por tipo.
- **`kafka.consumer.lag`** usa `topic` e `consumer_group` como labels, permitindo filtrar por topico e grupo de consumo.
- **`orders.backlog.current`** usa `status` para distinguir entre pedidos aguardando publicacao e pedidos com falha de publicacao.

---

## PromQL Essencial

### Funcoes fundamentais

#### `rate(counter[intervalo])`

Calcula a taxa **por segundo** de incremento de um counter, tratando automaticamente resets.

```promql
# Pedidos criados por segundo (ultimos 5 min)
rate(orders_created_total{result="created"}[5m])
```

Regra pratica: o intervalo deve ser pelo menos 4x o scrape interval (se scrape = 15s, use >= 1m).

#### `increase(counter[intervalo])`

Retorna o **incremento absoluto** no intervalo. Equivale a `rate() * duracao_em_segundos`.

```promql
# Pedidos criados na ultima hora
increase(orders_created_total[1h])
```

#### `histogram_quantile(quantil, buckets)`

Calcula o quantil (percentil) a partir dos buckets do histogram.

```promql
# P95 de latencia de criacao
histogram_quantile(0.95,
  sum by (le) (
    rate(orders_create_duration_milliseconds_bucket{result="created"}[5m])
  )
)

# P50 (mediana) de latencia de persistencia de notificacoes
histogram_quantile(0.50,
  sum by (le) (
    rate(notifications_persistence_duration_milliseconds_bucket{result="persisted"}[5m])
  )
)
```

O `sum by (le)` e **obrigatorio** para agregar entre instancias mantendo os buckets.

#### `sum by (label)` e `avg by (label)`

Agrega series por labels especificas.

```promql
# Taxa de erros por tipo de resultado no ProcessingWorker
sum by (result) (
  rate(orders_processed_total{result!="processed"}[5m])
)

# Lag medio por consumer_group
avg by (consumer_group) (kafka_consumer_lag)
```

#### `avg_over_time(gauge[intervalo])`

Media de um gauge ao longo do tempo.

```promql
# Media do backlog nos ultimos 30 minutos
avg_over_time(orders_backlog_current{status="PendingPublish"}[30m])
```

---

### Exemplos praticos com a PoC

#### Taxa de erro geral do OrderService

```promql
# Porcentagem de erros na criacao de pedidos
sum(rate(orders_created_total{result!="created"}[5m]))
/
sum(rate(orders_created_total[5m]))
* 100
```

#### Throughput end-to-end

```promql
# Comparar entrada vs saida do pipeline
# Entrada: pedidos criados
sum(rate(orders_created_total{result="created"}[5m]))
# Saida: notificacoes persistidas
sum(rate(notifications_persisted_total{result="persisted"}[5m]))
```

#### Deteccao de acumulo no pipeline

```promql
# Se o lag do consumer cresce consistentemente, ha acumulo
avg_over_time(kafka_consumer_lag{consumer_group="processing-worker"}[10m])
> avg_over_time(kafka_consumer_lag{consumer_group="processing-worker"}[10m] offset 10m)
```

#### Latencia media vs P95 do OrderService

```promql
# Media
rate(orders_create_duration_milliseconds_sum{result="created"}[5m])
/
rate(orders_create_duration_milliseconds_count{result="created"}[5m])

# P95
histogram_quantile(0.95,
  sum by (le) (
    rate(orders_create_duration_milliseconds_bucket{result="created"}[5m])
  )
)
```

Comparar media com P95 revela se ha **cauda longa** de latencia (poucos requests muito lentos puxando a media).

---

## Boas Praticas

### Cardinalidade

**Cardinalidade** e o numero total de combinacoes unicas de labels para uma metrica. Alta cardinalidade causa:

- Uso excessivo de memoria no Prometheus.
- Queries lentas.
- Custos elevados em solucoes de metricas pagas.

| Pratica | Exemplo bom | Exemplo ruim |
|---------|-------------|--------------|
| Labels com valores finitos | `result="created"` (enum) | `user_id="abc123"` (infinito) |
| Evitar IDs unicos como label | `status="PendingPublish"` | `order_id="550e8400..."` |
| Labels previsaveis | `http_method="POST"` | `request_body="..."` |

**Regra:** Se uma label pode ter mais de ~100 valores distintos, provavelmente nao deveria ser uma label de metrica. Use traces ou logs para dados de alta cardinalidade.

### Labels

- **Adicione labels que voce vai usar em queries e alertas.** Labels que ninguem filtra sao custo sem beneficio.
- **Use valores consistentes.** Padronize enums (ex: `created`, `validation_failed` -- nunca misture `Created` e `created`).
- **Nao repita informacao do nome da metrica nas labels.** Se a metrica se chama `orders_created_total`, nao adicione label `entity="order"`.

### O que NAO medir como metrica

| Nao medir | Por que | Alternativa |
|-----------|---------|-------------|
| IDs individuais (order_id, user_id) | Cardinalidade infinita | Use traces ou logs |
| Payloads ou mensagens de erro completas | Texto nao e numerico | Use logs |
| Dados que mudam a cada request sem padrao | Nao agrega informacao util | Use traces |
| Metricas que ninguem olha ou alerta | Custo sem beneficio | Remova |

---

## Avancado

### Exemplars

Exemplars sao **links de uma metrica para um trace especifico**. Quando uma metrica e registrada, o SDK pode anexar o `trace_id` e `span_id` da requisicao corrente como exemplar.

Na PoC, exemplars estao habilitados com a politica `AlwaysOn`, significando que toda observacao de metrica inclui um exemplar. Isso permite, no Grafana, clicar em um ponto de uma metrica e navegar diretamente para o trace correspondente no Tempo.

Para detalhes completos de implementacao, consulte a documentacao de exemplars do projeto.

### Recording Rules

Recording rules pre-calculam queries PromQL frequentes e as armazenam como novas time series. Uteis para:

- Queries caras usadas em dashboards (evita recalcular a cada refresh).
- Simplificar queries complexas em alertas.

```yaml
# Exemplo de recording rule (nao implementada na PoC)
groups:
  - name: orders
    rules:
      - record: orders:creation_rate:5m
        expr: sum(rate(orders_created_total{result="created"}[5m]))
      - record: orders:p95_latency:5m
        expr: |
          histogram_quantile(0.95,
            sum by (le) (
              rate(orders_create_duration_milliseconds_bucket{result="created"}[5m])
            )
          )
```

### Alerting Rules

Alerting rules avaliam expressoes PromQL periodicamente e disparam alertas quando condicoes sao atendidas.

```yaml
# Exemplo de alerta (simplificado)
groups:
  - name: orders-alerts
    rules:
      - alert: HighOrderCreationLatency
        expr: |
          histogram_quantile(0.95,
            sum by (le) (
              rate(orders_create_duration_milliseconds_bucket{result="created"}[5m])
            )
          ) > 500
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "P95 de latencia de criacao de pedidos acima de 500ms por 5 minutos"
```

---

## Referencias

### Documentacao oficial

- **Prometheus -- Metric Types:** [https://prometheus.io/docs/concepts/metric_types/](https://prometheus.io/docs/concepts/metric_types/)
- **Prometheus -- PromQL:** [https://prometheus.io/docs/prometheus/latest/querying/basics/](https://prometheus.io/docs/prometheus/latest/querying/basics/)
- **Prometheus -- Naming Best Practices:** [https://prometheus.io/docs/practices/naming/](https://prometheus.io/docs/practices/naming/)
- **Prometheus -- Histograms and Summaries:** [https://prometheus.io/docs/practices/histograms/](https://prometheus.io/docs/practices/histograms/)

### OpenTelemetry

- **OTel Metrics Specification:** [https://opentelemetry.io/docs/specs/otel/metrics/](https://opentelemetry.io/docs/specs/otel/metrics/)
- **OTel .NET Metrics:** [https://opentelemetry.io/docs/languages/dotnet/instrumentation/#metrics](https://opentelemetry.io/docs/languages/dotnet/instrumentation/#metrics)
- **OTel Exemplars Specification:** [https://opentelemetry.io/docs/specs/otel/metrics/data-model/#exemplars](https://opentelemetry.io/docs/specs/otel/metrics/data-model/#exemplars)

### Artigos

- **Robust Perception -- Prometheus Histograms:** [https://www.robustperception.io/how-does-a-prometheus-histogram-work/](https://www.robustperception.io/how-does-a-prometheus-histogram-work/)
- **Grafana Labs -- Introduction to PromQL:** [https://grafana.com/blog/2020/02/04/introduction-to-promql-the-prometheus-query-language/](https://grafana.com/blog/2020/02/04/introduction-to-promql-the-prometheus-query-language/)
