# 07 - Grafana: Dashboards e Alertas

## 1. Visao Geral

O **Grafana** e a plataforma de visualizacao e alerting da stack LGTM (Loki, Grafana, Tempo, Mimir/Prometheus). Na PoC, ele atua como interface unica para:

- Visualizar metricas coletadas pelo Prometheus (via OTel Collector)
- Explorar traces distribuidos armazenados no Tempo
- Consultar logs estruturados indexados pelo Loki
- Definir e gerenciar regras de alerta baseadas em metricas
- Correlacionar sinais via exemplars (metrica -> trace)

O Grafana roda como parte do container `lgtm` e esta disponivel em `http://localhost:3000`.

---

## 2. Dashboards

### 2.1. Conceitos Fundamentais

| Conceito | Descricao |
|---|---|
| **Panel** | Unidade basica de visualizacao. Cada panel executa uma ou mais queries e renderiza o resultado. |
| **Row** | Agrupador logico de panels. Pode ser colapsavel. |
| **Variable** | Parametro dinamico (dropdown) que filtra queries. Ex: `$service_name`. |
| **Time Range** | Janela temporal aplicada a todas as queries do dashboard. Padrao da PoC: `now-30m` a `now`. |
| **Refresh** | Intervalo de atualizacao automatica. Na PoC: `30s`. |

### 2.2. Tipos de Visualizacao

| Tipo | Uso Tipico |
|---|---|
| **Time Series** | Series temporais com linhas/areas. Ideal para latencia, throughput, lag. |
| **Stat** | Valor unico com coloracao por threshold. Ex: total de requests. |
| **Gauge** | Indicador com min/max. Ex: uso de CPU, porcentagem de erro. |
| **Bar Gauge** | Barras horizontais/verticais com thresholds. Usado na PoC para backlog. |
| **Table** | Dados tabulares. Util para listar top-N endpoints. |
| **Heatmap** | Distribuicao de valores ao longo do tempo. Ideal para histogramas de latencia. |

### 2.3. Como a PoC Provisiona Dashboards

O Grafana suporta **provisioning declarativo** via arquivos YAML e JSON montados no container.

**Estrutura de arquivos:**

```
infra/grafana/
  provisioning/
    dashboards/
      otel-poc-dashboards.yaml    # Provider config
    datasources/
      otel-poc-datasource-exemplars.yaml
    alerting/
      otel-poc-alert-rules.yaml
      otel-poc-contact-points.yaml
      otel-poc-notification-policies.yaml
  dashboards/
    otel-poc-overview.json         # JSON model do dashboard
```

**Provider config** (`otel-poc-dashboards.yaml`):

```yaml
apiVersion: 1

providers:
  - name: OTel PoC
    orgId: 1
    folder: OTel PoC
    type: file
    disableDeletion: false
    allowUiUpdates: false
    updateIntervalSeconds: 30
    options:
      path: /otel-lgtm/dashboards
      foldersFromFilesStructure: false
```

Campos importantes:
- `folder`: pasta logica no Grafana onde o dashboard aparece
- `allowUiUpdates: false`: impede edicoes via UI (forcando versionamento)
- `updateIntervalSeconds: 30`: Grafana re-le o JSON a cada 30s

### 2.4. Panels da PoC Explicados

O dashboard **"OTel PoC - Service Metrics"** (`uid: otel-poc-m3-overview`) possui 10 panels organizados em 4 linhas, cobrindo os 3 servicos.

#### Panel 1 - OrderService: Throughput de Criacao

- **Tipo:** Time Series (unidade: ops)
- **PromQL:**
  ```promql
  sum by (result) (rate(orders_created_total{service_name="order-service"}[5m]))
  ```
- **O que mostra:** Taxa de criacao de pedidos por segundo, segmentada por `result` (ex: `created`, `error`). Usa `rate()` sobre um counter para obter ops/s.

#### Panel 2 - OrderService: Latencia P50/P95

- **Tipo:** Time Series (unidade: ms)
- **PromQL (P50):**
  ```promql
  histogram_quantile(0.50, sum by (le) (rate(orders_create_duration_milliseconds_bucket{service_name="order-service",result="created"}[5m])))
  ```
- **PromQL (P95):**
  ```promql
  histogram_quantile(0.95, sum by (le) (rate(orders_create_duration_milliseconds_bucket{service_name="order-service",result="created"}[5m])))
  ```
- **O que mostra:** Percentis de latencia do endpoint `POST /orders`. O P95 e o SLI principal e tambem a base da regra de alerta (threshold: 500ms).
- **Como funciona:** `histogram_quantile` calcula o percentil a partir dos buckets do histograma. O `rate()` dentro garante que o calculo reflita a janela recente (5 minutos).

#### Panel 3 - OrderService: Backlog Atual por Status

- **Tipo:** Bar Gauge
- **PromQL:**
  ```promql
  sum by (status) (orders_backlog_current{service_name="order-service",status=~"pending_publish|publish_failed"})
  ```
- **O que mostra:** Quantidade atual de mensagens pendentes de publicacao na tabela outbox. Thresholds: verde (0), laranja (>1), vermelho (>5).

#### Panels 4-5 - ProcessingWorker: Throughput e Latencia

- Mesma estrutura dos panels 1-2, usando metricas `orders_processed_total` e `orders_processing_duration_milliseconds_bucket` com `service_name="processing-worker"`.

#### Panel 6 - ProcessingWorker: Kafka Consumer Lag

- **Tipo:** Time Series
- **PromQL:**
  ```promql
  sum by (topic, consumer_group) (kafka_consumer_lag{service_name="processing-worker",topic="orders"})
  ```
- **O que mostra:** Diferenca entre o offset mais recente do topico e o offset do consumer group. Thresholds: verde (0), laranja (>10), vermelho (>100). Este panel esta vinculado a regra de alerta de lag.

#### Panels 7-9 - NotificationWorker

- Throughput (`notifications_persisted_total`), latencia P50/P95 (`notifications_persistence_duration_milliseconds_bucket`) e consumer lag (`kafka_consumer_lag` no topico `notifications`).

#### Panel 10 - OrderService: Exemplars (Bucket Bruto)

- **Tipo:** Time Series (largura total: 24 colunas)
- **PromQL:**
  ```promql
  sum by (le) (rate(orders_create_duration_milliseconds_bucket{service_name="order-service",result="created"}[5m]))
  ```
- **Exemplars habilitados:** `"exemplar": true`
- **O que mostra:** Rate dos buckets do histograma com pontos de exemplar sobrepostos. Cada exemplar carrega um `trace_id` que permite navegar diretamente ao trace no Tempo.

---

## 3. Dashboard as Code

### 3.1. Por Que Versionar Dashboards

- **Reproducibilidade:** qualquer membro do time recria o ambiente identico com `docker compose up`
- **Code Review:** mudancas no dashboard passam por PR, evitando alteracoes acidentais
- **Rollback:** reverter um dashboard quebrado e um `git revert`
- **Auditoria:** historico completo de quem mudou o que e quando

### 3.2. Estrutura do Provisioning YAML

O arquivo YAML do provider define **onde** o Grafana busca os dashboards:

```yaml
providers:
  - name: OTel PoC          # Nome do provider
    folder: OTel PoC         # Pasta no Grafana
    type: file               # Leitura de arquivos locais
    options:
      path: /otel-lgtm/dashboards   # Caminho dentro do container
```

### 3.3. JSON Model do Dashboard

O JSON segue o schema do Grafana e contem:

| Campo | Descricao |
|---|---|
| `uid` | Identificador unico (`otel-poc-m3-overview`) |
| `title` | Nome exibido (`OTel PoC - Service Metrics`) |
| `panels[]` | Array de panels com queries, layout e opcoes |
| `time` | Time range padrao (`now-30m` a `now`) |
| `refresh` | Intervalo de auto-refresh (`30s`) |
| `tags` | Tags para busca (`otel-poc`, `m3`, `metrics`) |
| `schemaVersion` | Versao do schema do Grafana (39) |

### 3.4. Workflow de Edicao

1. **Editar no Grafana UI** (se `allowUiUpdates: true`) ou localmente no JSON
2. **Exportar JSON:** Settings > JSON Model > Copiar
3. **Salvar em** `infra/grafana/dashboards/otel-poc-overview.json`
4. **Commit e PR:** revisar diff do JSON, aprovar e mergear
5. **Aplicar:** `docker compose restart` ou aguardar `updateIntervalSeconds`

> Dica: ao exportar, remova campos `id` (auto-gerado) e `version` para evitar conflitos.

---

## 4. Alerting

### 4.1. Conceitos

| Conceito | Descricao |
|---|---|
| **Alert Rule** | Regra que define uma condicao (query + threshold) e quando disparar. |
| **Condition** | Expressao booleana avaliada periodicamente. Ex: `P95 > 500`. |
| **Evaluation Interval** | Frequencia de avaliacao. Na PoC: `30s`. |
| **For Duration** | Tempo que a condicao deve permanecer verdadeira antes de disparar. Na PoC: `1m`. |
| **Contact Point** | Destino da notificacao (webhook, email, Slack, PagerDuty, etc.). |
| **Notification Policy** | Roteamento que define qual contact point recebe qual alerta. |
| **Labels** | Metadados do alerta usados para roteamento e agrupamento. |

### 4.2. Contact Points

O Grafana suporta diversos tipos de contact point:

| Tipo | Uso |
|---|---|
| **Webhook** | POST HTTP para qualquer endpoint. Usado na PoC. |
| **Email** | Notificacao por email via SMTP. |
| **Slack** | Mensagem em canal Slack via webhook URL. |
| **PagerDuty** | Integracao com PagerDuty para on-call. |
| **Microsoft Teams** | Mensagem via incoming webhook. |

**Configuracao da PoC** (`otel-poc-contact-points.yaml`):

```yaml
contactPoints:
  - orgId: 1
    name: OTel PoC Local Webhook
    receivers:
      - uid: otel_poc_local_webhook
        type: webhook
        disableResolveMessage: false
        settings:
          url: http://alert-webhook-mock:8080/
          httpMethod: POST
```

O `alert-webhook-mock` e um servidor Python simples que registra todas as notificacoes recebidas, permitindo inspecao via `GET /requests`.

### 4.3. Notification Policies

A notification policy define **roteamento** e **agrupamento** dos alertas:

```yaml
policies:
  - orgId: 1
    receiver: OTel PoC Local Webhook
    group_by:
      - alertname
      - grafana_folder
    group_wait: 0s
    group_interval: 1m
    repeat_interval: 4h
```

| Parametro | Valor | Significado |
|---|---|---|
| `group_by` | `alertname`, `grafana_folder` | Alertas com mesmo nome e folder sao agrupados em uma unica notificacao. |
| `group_wait` | `0s` | Envia imediatamente ao primeiro alerta (sem aguardar agrupamento). |
| `group_interval` | `1m` | Aguarda 1 minuto antes de enviar novos alertas do mesmo grupo. |
| `repeat_interval` | `4h` | Re-envia a notificacao a cada 4 horas enquanto o alerta persistir. |

### 4.4. Regras de Alerta da PoC

#### Regra 1: OrderService P95 > 500ms

```yaml
uid: otel_poc_order_p95_high
title: OrderService P95 > 500 ms
```

- **Query (refId A):**
  ```promql
  histogram_quantile(0.95, sum by (le) (rate(orders_create_duration_milliseconds_bucket{service_name="order-service",result="created"}[5m])))
  ```
- **Reducao (refId B):** `reduce` com funcao `last` -- extrai o ultimo valor da serie
- **Threshold (refId C):** `> 500` -- dispara se o valor reduzido superar 500ms
- **For:** `1m` -- a condicao deve persistir por 1 minuto
- **No Data:** `OK` -- ausencia de dados nao dispara alerta
- **Labels:** `severity: warning`, `service: order-service`, `signal: latency`
- **Annotations:**
  - `summary`: Latencia P95 de criacao acima de 500 ms por 1 minuto
  - `description`: O caminho feliz de POST /orders permaneceu acima do alvo de latencia na janela avaliada

#### Regra 2: ProcessingWorker Lag > 100

```yaml
uid: otel_poc_processing_lag_high
title: ProcessingWorker lag > 100
```

- **Query (refId A):**
  ```promql
  sum by (topic, consumer_group) (kafka_consumer_lag{service_name="processing-worker",topic="orders"})
  ```
- **Reducao (refId B):** `reduce` com funcao `last`
- **Threshold (refId C):** `> 100`
- **For:** `1m`
- **No Data:** `OK`
- **Labels:** `severity: warning`, `service: processing-worker`, `signal: consumer_lag`
- **Annotations:**
  - `summary`: ProcessingWorker acumulou mais de 100 mensagens de lag por 1 minuto

Ambas as regras pertencem ao grupo `otel-poc-m3-alerts` com evaluation interval de `30s`, no folder `OTel PoC`.

---

## 5. Exemplars no Grafana

### 5.1. O Que Sao Exemplars

Exemplars sao **amostras individuais** anexadas a metricas de histograma. Cada exemplar carrega um `trace_id`, criando um link direto entre uma metrica agregada e um trace especifico. Visualmente, aparecem como **pontos clicaveis** sobrepostos em graficos time series.

### 5.2. Configuracao do Datasource

O datasource Prometheus precisa saber para onde redirecionar ao clicar em um exemplar. A configuracao da PoC (`otel-poc-datasource-exemplars.yaml`):

```yaml
datasources:
  - name: Prometheus
    uid: prometheus
    type: prometheus
    url: http://localhost:9090
    isDefault: true
    jsonData:
      exemplarTraceIdDestinations:
        - name: trace_id
          datasourceUid: tempo
        - name: traceID
          datasourceUid: tempo
```

Dois mapeamentos (`trace_id` e `traceID`) cobrem variantes de nomenclatura usadas por diferentes instrumentacoes.

### 5.3. Como Ativar no Panel

No panel editor do Grafana:

1. Selecionar a query desejada
2. Ativar o toggle **"Exemplars"** (abaixo do campo de query)
3. Salvar o dashboard

Na PoC, o panel 10 ("OrderService - Exemplars (bucket bruto)") ja tem `"exemplar": true` configurado no JSON.

### 5.4. Fluxo de Investigacao

1. **Observar spike** no grafico de latencia ou throughput
2. **Identificar exemplar** -- ponto marcado proximo ao spike
3. **Clicar no exemplar** -- Grafana exibe o `trace_id`
4. **Clicar em "Query with Tempo"** -- abre o trace completo no Tempo
5. **Analisar spans** -- identificar qual servico/operacao causou a latencia
6. **Correlacionar com logs** -- usar o `trace_id` no Loki para ver logs associados

---

## 6. Boas Praticas

### 6.1. Metodologias USE e RED

**USE Method** (Brendan Gregg) -- para **recursos** (CPU, memoria, disco, rede):

| Dimensao | Metrica |
|---|---|
| **U**tilization | % de uso do recurso |
| **S**aturation | Fila de trabalho pendente |
| **E**rrors | Contagem de erros |

**RED Method** (Tom Wilkie) -- para **servicos** (APIs, workers):

| Dimensao | Metrica |
|---|---|
| **R**ate | Requests por segundo |
| **E**rrors | Taxa de erro |
| **D**uration | Latencia (P50, P95, P99) |

A PoC segue o RED method: throughput (Rate), resultado por tipo incluindo erros (Errors) e percentis de latencia (Duration).

### 6.2. Organizacao de Dashboards

- **Um dashboard overview** com metricas-chave de todos os servicos (como na PoC)
- **Um dashboard por servico** para deep-dive (para ambientes maiores)
- **Dashboards de infraestrutura** separados (Kafka, PostgreSQL, etc.)

### 6.3. Performance

- Evitar mais de **15-20 panels** por dashboard (cada panel executa queries independentes)
- Usar **time range** apropriado (janelas muito longas com alta resolucao sobrecarregam o Prometheus)
- Preferir `rate()` com janelas de pelo menos `[2m]` para evitar gaps

### 6.4. Alertas: Evitar Alert Fatigue

- Alertar apenas sobre **sintomas**, nao causas (ex: "latencia alta" ao inves de "CPU alta")
- Usar **for duration** para evitar alertas transitorios (na PoC: `1m`)
- Definir **severity levels** claros:
  - `warning`: investigar em horario comercial
  - `critical`: acao imediata necessaria
- Agrupar alertas (`group_by`) para evitar enxurrada de notificacoes
- Revisar alertas periodicamente: se nunca dispara, e inutil; se sempre dispara, ajustar threshold

---

## 7. Referencias

- [Grafana Documentation](https://grafana.com/docs/grafana/latest/)
- [Grafana Alerting](https://grafana.com/docs/grafana/latest/alerting/)
- [Dashboard Best Practices](https://grafana.com/docs/grafana/latest/dashboards/build-dashboards/best-practices/)
- [Provisioning Grafana](https://grafana.com/docs/grafana/latest/administration/provisioning/)
- [USE Method - Brendan Gregg](https://www.brendangregg.com/usemethod.html)
- [RED Method - Tom Wilkie](https://grafana.com/blog/2018/08/02/the-red-method-how-to-instrument-your-services/)
- [Prometheus histogram_quantile](https://prometheus.io/docs/prometheus/latest/querying/functions/#histogram_quantile)
- [Grafana Exemplars](https://grafana.com/docs/grafana/latest/fundamentals/exemplars/)
