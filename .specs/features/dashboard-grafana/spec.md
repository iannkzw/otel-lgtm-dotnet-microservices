# Dashboard Grafana - Specification

**Milestone**: M3 - Metricas e Observabilidade Avancada
**Status**: Specified

---

## Problem Statement

A feature `metricas-customizadas` ja fechou a parte de coleta para a PoC: os tres
servicos exportam metricas via OTLP para o collector/LGTM e as series relevantes
ja aparecem normalizadas no backend Prometheus. O milestone M3, porem, ainda nao
tem uma visualizacao versionada e reproduzivel dessas metricas no Grafana, o que
dificulta demonstrar throughput, latencia, backlog e consumer lag de forma
consistente entre execucoes da PoC.

Esta feature precisa provisionar um dashboard Grafana focado apenas nos sinais ja
validados de `OrderService`, `ProcessingWorker` e `NotificationWorker`, sem
alterar a baseline funcional consolidada de M2/M3, sem criar novas metricas e
sem antecipar a feature posterior de alertas.

## Goals

- [ ] Especificar um dashboard Grafana versionado e provisionavel a partir do
      repositorio, usando o stack LGTM ja existente
- [ ] Definir um conjunto minimo de paineis para `OrderService`,
      `ProcessingWorker` e `NotificationWorker` cobrindo throughput, latencia,
      backlog e Kafka consumer lag
- [ ] Explicitar as queries PromQL esperadas a partir das series normalizadas ja
      validadas no backend
- [ ] Preservar a separacao de escopo entre dashboard e alertas, sem introduzir
      regras de alerta, contact points ou novos sinais
- [ ] Produzir criterios de aceite que permitam seguir com design e implementacao
      sem reabrir contratos, spans, logs, persistencia ou a pipeline OTLP

## Out of Scope

- Criacao de alertas Grafana, contact points, notification policies ou qualquer
  automacao de avaliacao
- Inclusao de novas metricas, novos labels customizados ou mudancas no catalogo
  estabilizado em `metricas-customizadas`
- Mudancas em spans, logs, payloads Kafka, contratos HTTP, persistencia ou fluxo
  funcional de M2/M3
- Reconfiguracao do collector, da exportacao OTLP ou do caminho LGTM ja validado
- Provisionamento de Loki/Tempo-specific dashboards, logs live tail ou links por
  exemplar nesta iteracao
- Painel operacional por particao Kafka, por `traceId`, por `orderId` ou por
  qualquer outra dimensao de alta cardinalidade

---

## Current Baseline

### Sinais ja validados no backend

- `orders_created_total`
- `orders_backlog_current`
- `orders_processed_total`
- `notifications_persisted_total`
- `kafka_consumer_lag`
- `orders_create_duration_milliseconds_bucket`,
  `orders_create_duration_milliseconds_sum`,
  `orders_create_duration_milliseconds_count`
- `orders_processing_duration_milliseconds_bucket`,
  `orders_processing_duration_milliseconds_sum`,
  `orders_processing_duration_milliseconds_count`
- `notifications_persistence_duration_milliseconds_bucket`,
  `notifications_persistence_duration_milliseconds_sum`,
  `notifications_persistence_duration_milliseconds_count`

### Contexto de labels e cardinalidade

- As dimensoes customizadas ja estabilizadas permanecem restritas a `result`,
  `status`, `topic` e `consumer_group`
- Labels de resource como `service_name`, `job` e `service_instance_id` devem
  ser tratados como contexto fornecido pelo backend, nao como novos eixos a
  serem criados pela feature
- Nao e permitido introduzir labels por `orderId`, `traceId`, `spanId`, payload,
  descricao ou qualquer identificador unico

### Dependencias existentes a preservar

- O dashboard deve consultar o datasource Prometheus do stack LGTM ja em uso
- A baseline de metricas continua vindo do caminho
  `servico -> otelcol -> lgtm/prometheus`
- O escopo de observabilidade desta feature e somente visualizacao de metricas;
  logs, traces e alertas permanecem fora da implementacao desta etapa

---

## Panel Catalog

## OrderService

### Painel: Throughput de criacao

- **Objective**: mostrar a taxa de pedidos criados por resultado agregado
- **Query expectation**:

```promql
sum by (result) (
  rate(orders_created_total{service_name="order-service"}[5m])
)
```

- **Visualization intent**: serie temporal ou bar chart simples por `result`
- **Notes**: `created` e a serie principal; `validation_failed`, `persist_failed`,
  `publish_failed` e `status_update_failed` aparecem como caminhos degradados

### Painel: Latencia de criacao P50/P95

- **Objective**: mostrar percentis de latencia do `POST /orders`
- **Query expectation**:

```promql
histogram_quantile(
  0.50,
  sum by (le) (
    rate(orders_create_duration_milliseconds_bucket{service_name="order-service",result="created"}[5m])
  )
)
```

```promql
histogram_quantile(
  0.95,
  sum by (le) (
    rate(orders_create_duration_milliseconds_bucket{service_name="order-service",result="created"}[5m])
  )
)
```

- **Visualization intent**: time series com P50 e P95 no mesmo painel
- **Notes**: P99 pode ficar como extensao de design/implementacao, mas nao e
  obrigatorio para o recorte minimo da feature

### Painel: Backlog atual por status

- **Objective**: mostrar pedidos retidos antes da publicacao no Kafka
- **Query expectation**:

```promql
sum by (status) (
  orders_backlog_current{service_name="order-service"}
)
```

- **Visualization intent**: stat ou bar gauge por `status`
- **Notes**: o painel deve expor apenas `pending_publish` e `publish_failed`

## ProcessingWorker

### Painel: Throughput de processamento

- **Objective**: mostrar taxa de mensagens tratadas por resultado agregado
- **Query expectation**:

```promql
sum by (result) (
  rate(orders_processed_total{service_name="processing-worker"}[5m])
)
```

- **Visualization intent**: serie temporal por `result`
- **Notes**: os resultados observados incluem `processed`, `invalid_payload`,
  `not_found`, `http_error`, `timeout`, `network_error`, `publish_failed` e
  `unexpected_error`

### Painel: Latencia de processamento P50/P95

- **Objective**: mostrar percentis do tempo total por mensagem tratada
- **Query expectation**:

```promql
histogram_quantile(
  0.50,
  sum by (le) (
    rate(orders_processing_duration_milliseconds_bucket{service_name="processing-worker",result="processed"}[5m])
  )
)
```

```promql
histogram_quantile(
  0.95,
  sum by (le) (
    rate(orders_processing_duration_milliseconds_bucket{service_name="processing-worker",result="processed"}[5m])
  )
)
```

- **Visualization intent**: time series com duas linhas
- **Notes**: o recorte minimo usa `result="processed"` para refletir o caminho
  feliz; o design pode prever filtros por `result` sem virar novo requisito

### Painel: Kafka consumer lag do topic orders

- **Objective**: mostrar atraso agregado de consumo do worker
- **Query expectation**:

```promql
sum by (topic, consumer_group) (
  kafka_consumer_lag{service_name="processing-worker",topic="orders"}
)
```

- **Visualization intent**: stat com sparkline ou time series unica
- **Notes**: o painel deve operar no nivel agregado do topic, nao por particao

## NotificationWorker

### Painel: Throughput de persistencia

- **Objective**: mostrar taxa de notificacoes persistidas ou rejeitadas
- **Query expectation**:

```promql
sum by (result) (
  rate(notifications_persisted_total{service_name="notification-worker"}[5m])
)
```

- **Visualization intent**: serie temporal por `result`
- **Notes**: os resultados relevantes incluem `persisted`, `invalid_payload`,
  `persistence_failed`, `consume_failed` e `unexpected_error`

### Painel: Latencia de persistencia P50/P95

- **Objective**: mostrar percentis de escrita no banco do ultimo hop
- **Query expectation**:

```promql
histogram_quantile(
  0.50,
  sum by (le) (
    rate(notifications_persistence_duration_milliseconds_bucket{service_name="notification-worker",result="persisted"}[5m])
  )
)
```

```promql
histogram_quantile(
  0.95,
  sum by (le) (
    rate(notifications_persistence_duration_milliseconds_bucket{service_name="notification-worker",result="persisted"}[5m])
  )
)
```

- **Visualization intent**: time series com P50 e P95
- **Notes**: o foco minimo e o caminho `persisted`; falhas continuam visiveis no
  painel de throughput por `result`

### Painel: Kafka consumer lag do topic notifications

- **Objective**: mostrar atraso agregado do ultimo consumidor da pipeline
- **Query expectation**:

```promql
sum by (topic, consumer_group) (
  kafka_consumer_lag{service_name="notification-worker",topic="notifications"}
)
```

- **Visualization intent**: stat com sparkline ou time series
- **Notes**: mesma semantica agregada do painel equivalente do `ProcessingWorker`

---

## User Stories

### P1: Provisionar um dashboard reprodutivel com as metricas ja validadas ⭐ MVP

**User Story**: Como mantenedor da PoC, quero um dashboard Grafana versionado e
provisionavel a partir do repositorio, para demonstrar M3 de forma repetivel sem
montagem manual de paineis no ambiente.

**Why P1**: Sem versionamento/provisionamento, a demo depende de configuracao
manual e nao fecha o milestone com reproducibilidade suficiente.

**Acceptance Criteria**:

1. WHEN a feature for implementada THEN o dashboard SHALL ser carregado pelo
   Grafana do stack LGTM a partir de arquivo versionado no repositorio
2. WHEN o ambiente subir com o datasource Prometheus disponivel THEN os paineis
   SHALL consultar apenas as metricas ja estabilizadas em `metricas-customizadas`
3. WHEN o dashboard for aberto THEN ele SHALL apresentar pelo menos um grupo de
   paineis para `OrderService`, `ProcessingWorker` e `NotificationWorker`
4. WHEN a baseline funcional de M2/M3 estiver saudavel THEN a feature SHALL nao
   exigir alteracoes em servicos .NET, payloads, spans, logs ou collector
5. WHEN a feature for validada THEN a demonstracao SHALL poder ser reproduzida em
   ambiente limpo sem criacao manual de paineis no UI do Grafana

**Independent Test**: Subir o ambiente, abrir o Grafana e confirmar que o
dashboard aparece automaticamente com os paineis definidos pela feature.

---

### P1: Exibir throughput e latencia minima por servico com queries normalizadas ⭐ MVP

**User Story**: Como operador da PoC, quero ver throughput e latencia dos tres
servicos em paineis consistentes, para avaliar rapidamente o comportamento do
fluxo feliz e dos caminhos degradados.

**Why P1**: Throughput e latencia sao o nucleo visual do milestone e dependem
diretamente da instrumentacao de metricas ja entregue.

**Acceptance Criteria**:

1. WHEN o dashboard consultar `OrderService` THEN ele SHALL mostrar throughput de
   `orders_created_total` por `result` e percentis P50/P95 de
   `orders_create_duration_milliseconds_bucket`
2. WHEN o dashboard consultar `ProcessingWorker` THEN ele SHALL mostrar
   throughput de `orders_processed_total` por `result` e percentis P50/P95 de
   `orders_processing_duration_milliseconds_bucket`
3. WHEN o dashboard consultar `NotificationWorker` THEN ele SHALL mostrar
   throughput de `notifications_persisted_total` por `result` e percentis P50/P95
   de `notifications_persistence_duration_milliseconds_bucket`
4. WHEN o backend Prometheus expuser nomes normalizados com underscores e
   sufixos `_milliseconds_*` THEN as queries SHALL refletir exatamente essa forma
   observada no LGTM
5. WHEN nao houver dados recentes para determinado `result` THEN o painel SHALL
   degradar de forma legivel, sem depender de novas metricas ou labels dinamicas

**Independent Test**: Gerar trafego real, abrir o dashboard e comparar os paineis
de throughput e latencia com queries equivalentes no Explore/Prometheus.

---

### P1: Exibir backlog e consumer lag agregados, sem alta cardinalidade ⭐ MVP

**User Story**: Como operador da PoC, quero ver backlog e lag agregados por
servico, para identificar rapidamente acumulo no `OrderService` e atraso de
consumo nos workers sem explosao de series.

**Why P1**: Esses sinais sao o complemento operacional minimo das metricas RED
da PoC e foram explicitamente estabilizados na feature anterior.

**Acceptance Criteria**:

1. WHEN o dashboard renderizar o `OrderService` THEN ele SHALL mostrar
   `orders_backlog_current` agregado apenas por `status`
2. WHEN o dashboard renderizar o `ProcessingWorker` THEN ele SHALL mostrar
   `kafka_consumer_lag` filtrado para `topic="orders"` e agregado por
   `topic` e `consumer_group`
3. WHEN o dashboard renderizar o `NotificationWorker` THEN ele SHALL mostrar
   `kafka_consumer_lag` filtrado para `topic="notifications"` e agregado por
   `topic` e `consumer_group`
4. WHEN os paineis exibirem labels THEN eles SHALL depender apenas das dimensoes
   ja estabilizadas e do contexto de resource existente no backend
5. WHEN houver backlog ou lag zerado THEN os paineis SHALL continuar legiveis e
   coerentes, sem necessidade de series por particao ou IDs de negocio

**Independent Test**: Gerar um caso de `publish_failed` e um backlog controlado
nos workers; validar backlog e lag no dashboard e no Explore com as mesmas
queries-base.

---

### P2: Padronizar organizacao visual para demonstracao segura da PoC

**User Story**: Como mantenedor da PoC, quero uma organizacao visual minima e
previsivel dos paineis, para reduzir regressao visual e facilitar a leitura da
demo em ambiente limpo.

**Why P2**: Nao e o nucleo do MVP, mas reduz risco de dashboard confuso ou pouco
demonstravel mesmo quando as queries estiverem corretas.

**Acceptance Criteria**:

1. WHEN a feature for desenhada/implementada THEN os paineis SHALL ficar
   organizados por servico ou secao equivalente claramente identificada
2. WHEN metricas de natureza diferente coexistirem THEN o layout SHALL separar
   throughput/latencia de backlog/lag para evitar ambiguidade visual
3. WHEN valores forem exibidos como stats ou gauges THEN a escolha do tipo de
   painel SHALL ser coerente com o comportamento esperado da metrica

**Independent Test**: Abrir o dashboard provisionado e verificar se a leitura
dos sinais minimos pode ser feita sem editar manualmente o layout.

---

## Edge Cases

- WHEN o datasource Prometheus estiver provisionado com nome/UID diferente do
  esperado THEN a feature SHALL tratar essa dependencia explicitamente no design
  e na implementacao, sem hardcode opaco nao documentado
- WHEN uma query de histogram estiver errada por diferenca de normalizacao no
  backend THEN a feature SHALL registrar claramente a forma validada em Explore
  antes de considerar o painel concluido
- WHEN uma serie existir apenas para alguns resultados e nao para outros THEN o
  dashboard SHALL continuar utilizavel sem inventar labels ou novas metricas
- WHEN o ambiente estiver sem trafego recente THEN os paineis SHALL manter estado
  legivel de ausencia de dados, sem serem tratados como falha funcional da feature
- WHEN houver crescimento de `service_instance_id` no backend THEN o dashboard
  SHALL evitar agregacoes que fragmentem inutilmente a leitura por instancia

## Validation Criteria

### Provisionamento

1. O dashboard deve ser descrito em artefato versionado do repositorio
2. O Grafana deve conseguir carregar esse artefato pelo mecanismo de
   provisionamento definido para o stack atual
3. A dependencia de datasource deve estar explicitada antes da implementacao

### Queries

1. Cada painel minimo deve ter pelo menos uma query-base PromQL registrada na
   feature
2. As queries devem usar apenas as series normalizadas ja validadas no backend
3. Histograms devem usar explicitamente os buckets `_milliseconds_bucket`
   correspondentes ao servico correto
4. Counters devem usar `rate(...)` em janela coerente para throughput

### Demo minima

1. Subir o ambiente com `docker compose up -d --build`
2. Gerar pelo menos um fluxo feliz completo com `POST /orders`
3. Abrir o dashboard no Grafana
4. Confirmar os paineis minimos de throughput, latencia e backlog/lag para os
   tres servicos
5. Confrontar pelo menos uma query de cada servico com o Explore/Prometheus

## Success Criteria

- [ ] Existe uma especificacao fechada do dashboard com paineis minimos por
      servico e queries PromQL esperadas
- [ ] A feature mantem separacao clara entre dashboard e alertas
- [ ] O proximo passo de design pode detalhar JSON, datasource e layout sem
      precisar redefinir o catalogo de metricas
- [ ] Nenhum requisito da feature depende de nova metrica, novo contrato ou
      alteracao da baseline funcional consolidada