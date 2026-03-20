# Alertas Grafana - Specification

**Milestone**: M3 - Metricas e Observabilidade Avancada
**Status**: Specified

---

## Problem Statement

A feature `dashboard-grafana` ja foi implementada e validada com dashboard
versionado, datasource Prometheus provisionado por `uid: prometheus` e queries
PromQL normalizadas para os tres servicos da PoC. O milestone M3, porem, ainda
nao fecha o ciclo operacional porque faltam regras de alerta reproduziveis que
avaliem os sinais ja existentes e encaminhem notificacoes para um destino local,
sem acoplar a PoC a canais externos reais.

Esta feature deve especificar a camada de alertas do Grafana como continuacao
direta do dashboard ja entregue: regras minimas de latencia P95 do
`OrderService` e de consumer lag do `ProcessingWorker`, com contact point local
e escopo estritamente configuracional. Nenhuma mudanca de aplicacao, coletor,
metricas, labels, contratos Kafka, payloads, persistencia ou pipelines OTLP deve
ser necessaria para entregar esta feature.

## Goals

- [ ] Especificar a feature `alertas-grafana` como extensao direta da baseline
      validada de `dashboard-grafana`, reutilizando datasource, queries e sinais
      ja estabilizados em M3
- [ ] Definir no minimo duas regras obrigatorias: latencia P95 do
      `OrderService` acima de 500 ms por 1 minuto e consumer lag do
      `ProcessingWorker` acima de 100 mensagens
- [ ] Definir a abordagem de notificacao com contact point local, priorizando
      `webhook` mock, log local ou equivalente simples e verificavel, sem Slack,
      email, PagerDuty ou outros canais reais
- [ ] Deixar explicita a separacao entre configuracao de alertas Grafana e
      qualquer mudanca nos servicos .NET, no collector, nos processors ou no
      catalogo de metricas
- [ ] Produzir criterios de aceite claros o bastante para a proxima etapa de
      design e para uma implementacao pequena, verificavel e sem regressao

## Out of Scope

- Criacao de novas metricas, novos labels, novos spans, novos logs estruturados
  ou qualquer ajuste de instrumentacao nos servicos .NET
- Alteracoes em `otelcol.yaml`, processors de sampling, pipelines OTLP,
  exporters, datasource Prometheus existente ou estrutura base do LGTM fora do
  necessario para provisionamento de alertas
- Mudancas em contratos HTTP, contratos Kafka, payloads, topicos, persistencia,
  schema, outbox, retry, DLQ ou comportamento funcional ja validado em M2/M3
- Alertas adicionais de `NotificationWorker` alem do catalogo minimo desta spec
- Contact points externos reais, integracoes SaaS reais ou credenciais externas
- Reformas do dashboard atual que nao sejam estritamente necessarias para manter
  coerencia de naming e navegacao da feature

---

## Current Baseline

### Dependencias ja validadas a reutilizar

- Dashboard versionado da PoC ja provisionado por arquivo no stack LGTM
- Datasource Prometheus existente e validado com `uid: prometheus`
- Queries normalizadas de histogram em Prometheus/Grafana usando
  `*_duration_milliseconds_*`
- Sinais minimos ja validados no backend:
  - `orders_created_total`
  - `orders_backlog_current`
  - `orders_processed_total`
  - `notifications_persisted_total`
  - `kafka_consumer_lag`
  - `orders_create_duration_milliseconds_bucket`
  - `orders_processing_duration_milliseconds_bucket`
  - `notifications_persistence_duration_milliseconds_bucket`

### Principios de escopo

- Alertas devem ser tratados como camada de configuracao do Grafana, nao como
  extensao de instrumentacao de aplicacao
- Queries de alerta devem partir das mesmas series ja usadas ou planejadas no
  dashboard, sem reinterpretar nomes canonicos do codigo
- A feature depende da baseline de `dashboard-grafana`, mas nao deve reabrir o
  escopo visual do dashboard alem do estritamente necessario

---

## Alert Catalog

## Regras obrigatorias

### Alerta 1: OrderService - Latencia P95 alta na criacao de pedidos

- **Signal**: `orders_create_duration_milliseconds_bucket`
- **Objective**: detectar degradacao sustentada do caminho feliz de
  `POST /orders`
- **Base query expectation**:

```promql
histogram_quantile(
  0.95,
  sum by (le) (
    rate(orders_create_duration_milliseconds_bucket{service_name="order-service",result="created"}[5m])
  )
)
```

- **Rule base expectation**: disparar quando o valor calculado ficar acima de
  `500` por pelo menos `1m`
- **Evaluation intent**: regra orientada ao ultimo valor agregado do P95, com
  avaliacao periodica curta e janela suficiente para reduzir ruido operacional
- **Why this matters**: cobre diretamente o objetivo de M3 de mostrar alerta em
  cima de um sinal RED ja presente no dashboard

### Alerta 2: ProcessingWorker - Consumer lag alto no topic orders

- **Signal**: `kafka_consumer_lag`
- **Objective**: detectar atraso sustentado do consumidor principal do pipeline
- **Base query expectation**:

```promql
sum by (topic, consumer_group) (
  kafka_consumer_lag{service_name="processing-worker",topic="orders"}
)
```

- **Rule base expectation**: disparar quando o lag agregado ficar acima de
  `100` mensagens por pelo menos `1m`
- **Evaluation intent**: regra agregada por `topic` e `consumer_group`, sem
  granularidade por particao
- **Why this matters**: cobre o principal sinal operacional de fila do worker
  intermediario da PoC sem introduzir novas metricas

## Alertas candidatos fora do minimo

- `NotificationWorker` com `kafka_consumer_lag{topic="notifications"}` acima de
  threshold futuro, apenas como extensao posterior de M3/M4
- Backlog anomalo do `OrderService` com `orders_backlog_current` por `status`, se
  houver necessidade operacional real apos fechar os dois alertas minimos

Esses alertas candidatos nao fazem parte do recorte obrigatorio desta spec.

---

## Contact Point Strategy

### Direcao principal

O contact point desta feature deve permanecer local ao ambiente da PoC. A
abordagem preferencial e um `webhook` mock simples e verificavel no ambiente
Docker, ou um destino equivalente local baseado em log quando o mock HTTP nao
for necessario para a primeira validacao.

### Regras para a abordagem local

- Nao usar Slack, Microsoft Teams, email, PagerDuty, Opsgenie ou qualquer outro
  canal externo real
- O destino deve ser reproduzivel em ambiente limpo e nao depender de secrets de
  terceiros
- A validacao precisa conseguir demonstrar pelo menos o caminho `firing` e,
  quando possivel, o retorno a `resolved`, usando um receptor local simples
- Se houver mais de uma opcao viavel no design, priorizar `webhook` mock por ser
  mais facil de inspecionar por payload e timestamps; `log` local fica como
  fallback aceitavel

### Consequencia de escopo

Qualquer artefato necessario para contact point, policy ou receiver local deve
continuar sendo tratado como configuracao de ambiente Grafana/local helper, nunca
como alteracao dos servicos de negocio ou da telemetria emitida por eles.

---

## Acceptance Criteria

- Existe uma especificacao versionada da feature em
  `.specs/features/alertas-grafana/spec.md`, conectada explicitamente a
  `dashboard-grafana` como predecessora direta
- O catalogo minimo inclui, de forma nao ambigua, os dois alertas obrigatorios:
  P95 do `OrderService` acima de `500 ms` por `1m` e lag do
  `ProcessingWorker` acima de `100` mensagens por `1m`
- Cada alerta obrigatorio explicita pelo menos: serie base, query PromQL base,
  threshold, janela temporal e intencao de agregacao
- A especificacao define que o datasource a reutilizar e o existente com
  `uid: prometheus`, sem criar datasource novo
- A especificacao define que contact point e notification routing devem ser
  locais ao ambiente da PoC, sem acoplamento a canais externos reais
- Fica explicito que a implementacao deve permanecer no plano de configuracao de
  alertas Grafana e nao pode exigir mudancas em metricas, labels, collector,
  processors, pipelines OTLP, contratos Kafka, payloads, persistencia ou
  servicos .NET
- Qualquer mudanca opcional em dashboard existente fica marcada como nao
  obrigatoria para concluir a feature

---

## Validation Intent For Future Steps

Esta especificacao considera suficiente, para as proximas etapas de design e
implementacao, que a validacao futura consiga:

1. provisionar regras e contact point local de forma reproduzivel no Grafana;
2. demonstrar o alerta de latencia com carga ou atraso controlado sem alterar os
   contratos dos servicos;
3. demonstrar o alerta de lag com acumulacao controlada de mensagens no
   `ProcessingWorker` sem alterar metricas nem labels;
4. comprovar que os alertas usam os sinais ja existentes no datasource
   Prometheus provisionado da PoC.