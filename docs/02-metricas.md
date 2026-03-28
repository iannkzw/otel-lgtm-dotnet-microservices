# Metricas

## Sumario

1. [O que sao Metricas](#o-que-sao-metricas)
2. [Tipos de Metricas](#tipos-de-metricas)
3. [Percentis: P50, P95, P99](#percentis-p50-p95-p99)
4. [Nomenclatura Prometheus](#nomenclatura-prometheus)
5. [Metricas da PoC](#metricas-da-poc)
6. [PromQL Essencial](#promql-essencial)
7. [Boas Praticas](#boas-praticas)
8. [Avancado](#avancado)
9. [Referencias](#referencias)

---

## O que sao Metricas

Metricas sao **valores numericos medidos ao longo do tempo**. Pense nelas como um painel de instrumentos de um carro: velocidade atual, temperatura do motor, nivel de combustivel. Cada indicador e um numero que e amostrado periodicamente e armazenado para analise posterior.

Tecnicamente, cada metrica e armazenada como uma **time series** — uma sequencia de pares `(timestamp, valor)` identificada por um nome e um conjunto de **labels** (chave-valor que funcionam como filtros).

```
# Leitura: "as 19:00:00, o OrderService criou 42 pedidos com sucesso"
orders_created_total{result="created", service="OrderService"} 42  @1711324800

# Leitura: "15 segundos depois, eram 43"
orders_created_total{result="created", service="OrderService"} 43  @1711324815
```

O Prometheus (nosso banco de dados de metricas) coleta esses valores a cada 15 segundos (scrape interval) e os armazena para que possamos consultar qualquer periodo do passado.

### Por que usar metricas?

| Propriedade | Descricao |
|------------|-----------|
| **Baixo custo** | Sao apenas numeros agregados; muito mais leves que logs ou traces |
| **Rapidas para consultar** | Ideais para dashboards em tempo real e alertas |
| **Ideais para alertas** | "Se o P95 de latencia superar 500ms por 5 minutos, me avise" |
| **Dimensionais** | Labels permitem fatiar os dados (ex: por servico, por tipo de erro) |

**Limitacao importante:** metricas nao guardam detalhes individuais. Elas dizem "houve 10 erros", mas nao "qual foi a mensagem de erro do request X". Para isso, use logs ou traces.

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

Um counter e um valor que **so cresce** (ou reseta para zero quando o processo reinicia). Pense no odometro de um carro: ele nunca volta atras.

**Consequencia pratica:** nunca faz sentido olhar o valor absoluto de um counter. Se o counter tem valor 1042, isso nao diz nada — o que importa e *quantos eventos ocorreram no ultimo intervalo*. Para isso usamos `rate()` e `increase()`.

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

Um gauge e um valor que **sobe e desce** livremente. Representa o estado *atual* de algo — como o nivel de combustivel ou a quantidade de pedidos na fila agora.

Ao contrario do counter, o valor absoluto de um gauge e util e pode ser lido diretamente.

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

Um histogram e o tipo mais sofisticado. Ele serve para medir **distribuicoes de valores** — tipicamente duracoes de operacoes ou tamanhos de payloads.

**Por que nao usar um gauge ou counter para latencia?**

Imagine que voce quer saber a latencia do seu servico. Um gauge com "latencia atual" mostra apenas o ultimo request. Um counter nao faz sentido para latencia. O que voce realmente quer saber e: "a maioria dos requests esta rapida, mas quantos estao lentos?"

O histogram resolve isso dividindo os valores em **buckets** (faixas). Por exemplo:

```
Bucket <= 50ms:  850 requests  (a maioria e rapida)
Bucket <= 100ms: 930 requests
Bucket <= 250ms: 980 requests
Bucket <= 500ms: 995 requests
Bucket <= +Inf:  1000 requests (total)
```

Com essa distribuicao, conseguimos calcular percentis — veja a secao [Percentis](#percentis-p50-p95-p99) para entender o que sao.

O Prometheus armazena tres time series para cada histogram:

- `_bucket{le="X"}` — contagem acumulada de observacoes <= X
- `_sum` — soma de todos os valores observados
- `_count` — total de observacoes

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

## Percentis: P50, P95, P99

Esta e uma das partes mais importantes para entender metricas de performance. Se voce ja viu "P95 de latencia" em algum dashboard mas nao sabia o que significava, esta secao e para voce.

### O que e um percentil?

Um **percentil** divide uma distribuicao de valores em partes. O percentil X significa: "X% das observacoes ficaram abaixo desse valor".

**Exemplo concreto:** imagine que voce mediu a latencia de 1000 requests e ordenou os valores do menor para o maior.

```
P50 (Mediana) = 45ms   → 500 requests foram mais rapidos que 45ms
P95           = 320ms  → 950 requests foram mais rapidos que 320ms
P99           = 850ms  → 990 requests foram mais rapidos que 850ms
```

Interpretando:
- **P50 = 45ms**: metade dos seus usuarios teve latencia abaixo de 45ms. E a experiencia "tipica".
- **P95 = 320ms**: 95% dos usuarios tiveram latencia abaixo de 320ms. Os 5% mais lentos esperaram mais que isso.
- **P99 = 850ms**: 99% dos usuarios tiveram latencia abaixo de 850ms. O 1% mais lento esperou quase 1 segundo.

### Por que nao usar a media?

A media e enganosa. Considere dois cenarios:

```
Cenario A: 990 requests em 10ms, 10 requests em 5000ms
  → Media: (990 * 10 + 10 * 5000) / 1000 = 59,9ms  (parece ok)
  → P99: 5000ms  (1% dos usuarios esperou 5 segundos!)

Cenario B: todos os 1000 requests em 60ms
  → Media: 60ms
  → P99: 60ms
```

Ambos tem media ~60ms, mas o Cenario A tem um problema serio que a media esconde. Os percentis revelam a **cauda da distribuicao** — os usuarios que tiveram a pior experiencia.

**Convencao de mercado:**
- **P50** — experiencia tipica do usuario
- **P95** — pior caso que a maioria dos usuarios enfrenta. SLOs comuns usam P95 < 200ms para APIs web.
- **P99** — casos extremos. Util para detectar timeouts e erros que afetam uma minoria mas podem ser criticos.

### Como calcular percentis com Histogram

No Prometheus, usamos `histogram_quantile()`. O valor do quantil e o percentil dividido por 100:

```promql
# P95 = quantil 0.95
histogram_quantile(0.95,
  sum by (le) (
    rate(orders_create_duration_milliseconds_bucket[5m])
  )
)

# P50 = quantil 0.50 (mediana)
histogram_quantile(0.50,
  sum by (le) (
    rate(orders_create_duration_milliseconds_bucket[5m])
  )
)
```

O `sum by (le)` e obrigatorio para agregar os buckets de todas as instancias do servico antes de calcular o percentil.

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

PromQL (Prometheus Query Language) e a linguagem usada para consultar metricas. Aqui estao as funcoes que voce vai usar no dia a dia.

### Funcoes fundamentais

#### `rate(counter[intervalo])`

Calcula a **taxa por segundo** de incremento de um counter, tratando automaticamente resets do processo.

```promql
# "Quantos pedidos sao criados por segundo, em media, nos ultimos 5 minutos?"
rate(orders_created_total{result="created"}[5m])
```

**Por que `[5m]`?** O intervalo define a janela de tempo usada para calcular a taxa. Janelas muito curtas sao ruidosas; muito longas, lentas para reagir. Regra pratica: use pelo menos 4x o scrape interval (scrape = 15s → use >= 1m).

#### `increase(counter[intervalo])`

Retorna o **incremento absoluto** no intervalo. Mais intuitivo que `rate()` quando voce quer saber "quantos eventos aconteceram".

```promql
# "Quantos pedidos foram criados na ultima hora?"
increase(orders_created_total[1h])
```

`increase()` e equivalente a `rate() * duracao_em_segundos`. Use `rate()` para dashboards de throughput (eventos/s) e `increase()` para contagens em um periodo.

#### `histogram_quantile(quantil, buckets)`

Calcula o percentil a partir dos buckets de um histogram. Veja a secao [Percentis](#percentis-p50-p95-p99) para a explicacao conceitual.

```promql
# "Qual e a latencia que 95% dos requests de criacao de pedido ficam abaixo?"
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

O `sum by (le)` e **obrigatorio**: ele soma os buckets de todas as replicas do servico antes de calcular o percentil. Sem ele, o resultado seria o percentil de apenas uma instancia.

#### `sum by (label)` e `avg by (label)`

Agrega multiplas time series em uma so, agrupando por labels.

```promql
# "Qual e a taxa de erro de cada tipo no ProcessingWorker?"
sum by (result) (
  rate(orders_processed_total{result!="processed"}[5m])
)

# "Qual e o lag medio de cada consumer group?"
avg by (consumer_group) (kafka_consumer_lag)
```

#### `avg_over_time(gauge[intervalo])`

Media de um gauge ao longo do tempo. Util para suavizar oscilacoes.

```promql
# "Qual foi o backlog medio nos ultimos 30 minutos?"
avg_over_time(orders_backlog_current{status="PendingPublish"}[30m])
```

---

### Exemplos praticos com a PoC

#### Taxa de erro geral do OrderService

```promql
# "Qual porcentagem das tentativas de criacao esta falhando?"
sum(rate(orders_created_total{result!="created"}[5m]))
/
sum(rate(orders_created_total[5m]))
* 100
```

#### Throughput end-to-end

```promql
# Comparar entrada vs saida do pipeline
# Entrada: pedidos criados com sucesso
sum(rate(orders_created_total{result="created"}[5m]))

# Saida: notificacoes persistidas com sucesso
sum(rate(notifications_persisted_total{result="persisted"}[5m]))
```

Se a entrada for consistentemente maior que a saida, ha acumulo no pipeline.

#### Deteccao de acumulo no pipeline

```promql
# "O lag do consumer esta crescendo?" (compara o lag agora com 10 minutos atras)
avg_over_time(kafka_consumer_lag{consumer_group="processing-worker"}[10m])
> avg_over_time(kafka_consumer_lag{consumer_group="processing-worker"}[10m] offset 10m)
```

#### Latencia media vs P95 do OrderService

```promql
# Media (pode ser enganosa — veja a secao de Percentis)
rate(orders_create_duration_milliseconds_sum{result="created"}[5m])
/
rate(orders_create_duration_milliseconds_count{result="created"}[5m])

# P95 (mostra a experiencia do usuario nos piores 5% dos casos)
histogram_quantile(0.95,
  sum by (le) (
    rate(orders_create_duration_milliseconds_bucket{result="created"}[5m])
  )
)
```

Comparar media com P95 revela se ha **cauda longa** de latencia: se P95 >> media, ha poucos requests muito lentos que nao aparecem na media mas afetam uma parcela dos usuarios.

---

## Boas Praticas

### Cardinalidade

**Cardinalidade** e o numero total de combinacoes unicas de labels para uma metrica. Por exemplo, se uma metrica tem label `result` com 5 valores possiveis e label `service` com 3 valores, sua cardinalidade e 5 x 3 = 15 time series.

Alta cardinalidade e um dos problemas mais comuns em producao e causa:

- Uso excessivo de memoria no Prometheus.
- Queries lentas.
- Custos elevados em solucoes de metricas pagas.

| Pratica | Exemplo bom | Exemplo ruim |
|---------|-------------|--------------|
| Labels com valores finitos | `result="created"` (enum com ~5 valores) | `user_id="abc123"` (infinito) |
| Evitar IDs unicos como label | `status="PendingPublish"` | `order_id="550e8400..."` |
| Labels previsaveis | `http_method="POST"` | `request_body="..."` |

**Regra:** Se uma label pode ter mais de ~100 valores distintos, provavelmente nao deveria ser uma label de metrica. Use traces ou logs para dados de alta cardinalidade.

### Labels

- **Adicione labels que voce vai usar em queries e alertas.** Labels que ninguem filtra sao custo sem beneficio.
- **Use valores consistentes.** Padronize enums (ex: `created`, `validation_failed` — nunca misture `Created` e `created`).
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

Na PoC, exemplars estao habilitados com a politica `AlwaysOn`, significando que toda observacao de metrica inclui um exemplar. Isso permite, no Grafana, clicar em um ponto de um grafico de latencia e navegar diretamente para o trace do request que causou aquele pico — conectando metricas e traces de forma pratica.

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

Leitura do alerta: "se 95% dos requests de criacao de pedido estao levando mais de 500ms, e essa condicao persiste por 5 minutos consecutivos, dispare um alerta de warning."

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
