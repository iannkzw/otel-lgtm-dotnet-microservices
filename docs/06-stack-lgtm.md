# 06 - Stack LGTM

## Indice

1. [O que e a Stack LGTM](#1-o-que-e-a-stack-lgtm)
2. [Papel de cada componente](#2-papel-de-cada-componente)
3. [Arquitetura](#3-arquitetura)
4. [Prometheus](#4-prometheus)
5. [Loki](#5-loki)
6. [Tempo](#6-tempo)
7. [Grafana](#7-grafana)
8. [Comparacao com alternativas](#8-comparacao-com-alternativas)
9. [Quando usar LGTM vs alternativas](#9-quando-usar-lgtm-vs-alternativas)
10. [Referencias](#10-referencias)

---

## 1. O que e a Stack LGTM

LGTM e o acronimo para **Loki + Grafana + Tempo + Mimir**, a stack de observabilidade open source da Grafana Labs. Cada componente e responsavel por um sinal de telemetria:

- **L**oki: logs
- **G**rafana: visualizacao e correlacao
- **T**empo: traces
- **M**imir: metricas (armazenamento de longo prazo compativel com Prometheus)

> **Na PoC:** usamos **Prometheus** em vez de Mimir para armazenamento de metricas. O Mimir e recomendado para producao em larga escala, mas o Prometheus e suficiente para cenarios menores e demonstracoes. A imagem Docker `grafana/otel-lgtm` empacota Grafana, Prometheus, Loki e Tempo em um unico container.

---

## 2. Papel de cada componente

| Componente | Sinal | Funcao | Query Language | Porta padrao |
|------------|-------|--------|----------------|-------------|
| **Prometheus** | Metricas | Coleta, armazena e consulta time series | PromQL | 9090 |
| **Loki** | Logs | Armazena e consulta logs com indexacao por labels | LogQL | 3100 |
| **Tempo** | Traces | Armazena e consulta traces distribuidos | TraceQL | 3200 |
| **Grafana** | Visualizacao | Dashboards, alertas e correlacao entre sinais | N/A (usa os acima) | 3000 |

---

## 3. Arquitetura

### 3.1 All-in-one: como a PoC usa

A PoC utiliza a imagem `grafana/otel-lgtm`, que empacota todos os componentes em um unico container Docker. Isso simplifica o setup para desenvolvimento e demonstracoes.

```
+------------------+     OTLP gRPC      +----------------+     OTLP HTTP     +---------------------------+
|  OrderService    |-------------------->|                |------------------>|  grafana/otel-lgtm        |
+------------------+     port 4317      |                |     port 4318     |                           |
                                         |  OTel          |                   |  +---------------------+  |
+------------------+                     |  Collector     |  traces --------->|  | Tempo               |  |
|  ProcessingWorker|-------------------->|                |  logs ----------->|  | Loki                |  |
+------------------+                     |  (otelcol)     |  metrics -------->|  | Prometheus          |  |
                                         |                |                   |  | Grafana (:3000)     |  |
+------------------+                     |                |                   |  +---------------------+  |
|  NotificationWkr |-------------------->|                |                   |                           |
+------------------+                     +----------------+                   +---------------------------+
```

**Fluxo de dados:**

1. Os 3 servicos .NET enviam telemetria via **OTLP gRPC** (porta 4317) para o **OTel Collector**
2. O Collector processa (memory limiter, tail sampling, batch) e reenvia via **OTLP HTTP** (porta 4318)
3. O container LGTM recebe e roteia: traces para **Tempo**, logs para **Loki**, metricas para **Prometheus**
4. **Grafana** consulta os tres backends e exibe dashboards unificados

### 3.2 Microservices mode (producao)

Em producao, cada componente roda como servico independente (ou cluster), permitindo:

- **Escalabilidade horizontal** independente por componente
- **Alta disponibilidade** com replicas
- **Armazenamento distribuido** (object storage: S3, GCS, Azure Blob)
- **Multi-tenancy** com isolamento por organizacao

```
                    +-- Tempo (cluster) --> Object Storage
OTel Collector -----|-- Loki (cluster)  --> Object Storage
                    +-- Mimir (cluster) --> Object Storage

Grafana (HA) -----> consulta todos os backends
```

> **Nota:** Em producao, Mimir substitui o Prometheus para armazenamento de metricas de longo prazo com alta disponibilidade.

---

## 4. Prometheus

### Modelo pull vs push

Tradicionalmente, o Prometheus usa o modelo **pull**: ele faz scrape de endpoints `/metrics` das aplicacoes em intervalos regulares.

Na PoC, usamos o modelo **push** via OTLP: as aplicacoes enviam metricas ao OTel Collector, que repassa ao Prometheus via OTLP HTTP. Isso e possivel porque o Prometheus suporta o protocolo OTLP como receiver nativo.

| Modelo | Como funciona | Usado na PoC? |
|--------|---------------|---------------|
| Pull (scrape) | Prometheus busca metricas periodicamente | Apenas para metricas internas do Collector |
| **Push (OTLP)** | **Aplicacao envia metricas via Collector** | **Sim, para os 3 servicos** |

### Armazenamento de time series

O Prometheus armazena dados como **time series**: sequencias de pares (timestamp, valor) identificadas por um nome de metrica e labels.

```
http_server_request_duration_seconds_bucket{service_name="order-service", http_route="/api/orders", le="0.5"} 142
```

- **Nome**: `http_server_request_duration_seconds_bucket`
- **Labels**: `service_name`, `http_route`, `le`
- **Valor**: `142`

### PromQL

PromQL e a linguagem de consulta do Prometheus. Para detalhes e exemplos praticos, consulte o **doc 02**.

Exemplos rapidos:

```promql
# Taxa de requisicoes por segundo
rate(http_server_request_duration_seconds_count{service_name="order-service"}[5m])

# Latencia p99
histogram_quantile(0.99, rate(http_server_request_duration_seconds_bucket{service_name="order-service"}[5m]))

# Taxa de erros
sum(rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m]))
/
sum(rate(http_server_request_duration_seconds_count[5m]))
```

---

## 5. Loki

### Arquitetura log-based

Diferente de solucoes como Elasticsearch que indexam o **conteudo** completo dos logs, o Loki indexa apenas os **labels** (metadados). O conteudo do log e armazenado comprimido e consultado via scan no momento da query.

| Aspecto | Loki | Elasticsearch |
|---------|------|---------------|
| Indexacao | Apenas labels | Conteudo completo (full-text) |
| Custo de armazenamento | Baixo | Alto |
| Velocidade de ingestao | Alta | Moderada |
| Velocidade de busca por conteudo | Moderada (scan) | Alta (indice invertido) |
| Complexidade operacional | Baixa | Alta |

Essa abordagem torna o Loki significativamente mais barato e simples de operar, com o tradeoff de queries por conteudo serem mais lentas em volumes muito grandes.

### Labels no Loki

Na PoC, os resource attributes do OpenTelemetry se tornam labels no Loki:

- `service_name`: `order-service`, `processing-worker`, `notification-worker`
- `level`: `Information`, `Warning`, `Error`

> **Cuidado:** Labels com alta cardinalidade (ex: `user_id`, `order_id`) degradam a performance do Loki. Use filtros de linha (`|=`, `| json`) para esses campos.

### LogQL

Para referencia completa de LogQL com exemplos da PoC, consulte o **doc 04**.

---

## 6. Tempo

### Backend de traces

Tempo e um backend de traces de alta escala que **nao requer indexacao**. Ele armazena traces completos e os busca por `trace_id`. Isso o torna extremamente eficiente em custo de armazenamento.

### Como traces chegam ao Tempo na PoC

```
App .NET --> OTLP gRPC --> OTel Collector --> OTLP HTTP --> Tempo
```

### TraceQL basico

TraceQL e a linguagem de consulta do Tempo, permitindo buscar traces por atributos dos spans:

```traceql
# Buscar por trace_id
{ trace:id = "abc123def456..." }

# Spans com erro
{ status = error }

# Spans do OrderService com latencia > 500ms
{ resource.service.name = "order-service" && duration > 500ms }

# Spans HTTP com status 500
{ span.http.response.status_code = 500 }

# Spans de banco de dados lentos
{ span.db.system = "postgresql" && duration > 1s }

# Combinando servicos
{ resource.service.name = "order-service" } >> { resource.service.name = "processing-worker" }
```

### Integracao com Grafana

No Grafana, o Tempo aparece como datasource com suporte a:

- **Busca por Trace ID**: cole o ID e visualize o trace completo (waterfall view)
- **TraceQL query**: busque traces por atributos
- **Service graph**: visualize dependencias entre servicos automaticamente
- **Correlacao**: clique em um span para ver logs (Loki) ou metricas (Prometheus) relacionados

---

## 7. Grafana

### Datasources

Na PoC, o Grafana vem pre-configurado com tres datasources:

| Datasource | Tipo | Funcao |
|------------|------|--------|
| Prometheus | Metricas | Consulta metricas via PromQL |
| Loki | Logs | Consulta logs via LogQL |
| Tempo | Traces | Consulta traces via TraceQL |

### Dashboards

Dashboards sao paineis configurados para visualizar dados de um ou mais datasources. A PoC inclui dashboards pre-configurados. Para detalhes de configuracao de dashboards, consulte o **doc 07**.

### Alerting

O Grafana permite configurar alertas baseados em qualquer datasource:

- **Metricas**: "Alerte se taxa de erros > 5% por 5 minutos"
- **Logs**: "Alerte se count de logs `Critical` > 0 em 1 minuto"
- **Canais**: email, Slack, PagerDuty, webhooks, etc.

### Correlacao entre sinais

A correlacao e o diferencial da stack LGTM. O Grafana permite navegar entre sinais sem sair da interface:

```
Metrica (Prometheus)                 Trace (Tempo)                    Log (Loki)
      |                                   |                               |
      +-- exemplar (trace_id) ----------->|                               |
      |                                   +-- traceId ------------------>|
      |                                   |<-- traceId ------------------+
      |<-- metrica relacionada -----------+                               |
```

**Fluxo tipico de investigacao:**

1. Dashboard mostra **pico de latencia** (metrica no Prometheus)
2. Clique no **exemplar** sobrepostos no grafico
3. Grafana abre o **trace completo** no Tempo (waterfall view)
4. Identifique o span lento; clique para ver **logs** daquele span no Loki
5. Logs mostram a causa raiz (ex: timeout no banco de dados)

Para detalhes sobre como configurar essa correlacao, consulte o **doc 07**.

---

## 8. Comparacao com alternativas

### Tabela comparativa

| Criterio | LGTM (PoC) | ELK (Elasticsearch + Logstash + Kibana) | Datadog / New Relic (SaaS) | Jaeger + Prometheus + ELK |
|----------|-----------|----------------------------------------|---------------------------|--------------------------|
| **Custo de licenca** | Gratuito (open source) | Gratuito (open source) / Pago (Elastic Cloud) | Pago por volume (pode ser alto) | Gratuito (open source) |
| **Custo operacional** | Baixo-Moderado | Alto (Elasticsearch e exigente) | Nenhum (gerenciado) | Alto (3 stacks distintas) |
| **Metricas** | Prometheus/Mimir (nativo) | Requer integracao adicional | Nativo | Prometheus (separado) |
| **Logs** | Loki (leve, labels) | Elasticsearch (full-text, poderoso) | Nativo | Elasticsearch (separado) |
| **Traces** | Tempo (eficiente) | APM Elastic (pago) | Nativo | Jaeger (separado) |
| **Correlacao de sinais** | Nativa (Grafana unifica tudo) | Parcial (Kibana + APM) | Nativa (plataforma unica) | Manual (3 interfaces) |
| **Escalabilidade** | Alta (microservices mode) | Alta (mas custosa) | Ilimitada (SaaS) | Variavel por componente |
| **Complexidade de setup** | Baixa (all-in-one) a Moderada | Alta | Muito baixa (SaaS) | Muito alta |
| **Vendor lock-in** | Nenhum | Baixo | Alto | Nenhum |
| **Comunidade** | Grande e crescente | Muito grande | N/A | Fragmentada |

### Detalhamento

**LGTM vs ELK:**

- Loki e significativamente mais barato que Elasticsearch para logs (nao indexa conteudo)
- Elasticsearch oferece busca full-text mais rapida para queries complexas
- Grafana oferece correlacao nativa entre metricas, logs e traces; Kibana precisa de plugins adicionais
- ELK requer tuning constante de JVM, shards e indices

**LGTM vs Datadog/New Relic:**

- SaaS elimina complexidade operacional, mas custo cresce linearmente com volume
- Em escala, LGTM pode ser 5-10x mais barato
- SaaS oferece features avancadas out-of-the-box (AI/ML, profiling)
- LGTM nao tem vendor lock-in; migracao de SaaS e custosa

**LGTM vs Jaeger + Prometheus + ELK:**

- LGTM oferece correlacao nativa em uma unica interface (Grafana)
- A combinacao Jaeger+Prometheus+ELK requer 3 interfaces distintas e integracao manual
- LGTM e mais simples de operar como stack unificada

---

## 9. Quando usar LGTM vs alternativas

| Cenario | Recomendacao | Justificativa |
|---------|-------------|---------------|
| Startup / equipe pequena, sem budget | **LGTM** | Gratuito, simples de operar, all-in-one |
| Empresa com equipe de infra dedicada | **LGTM** (microservices mode) | Controle total, sem custo de licenca |
| Equipe sem experiencia em infra | **Datadog/New Relic** | Setup zero, suporte incluido |
| Busca full-text avancada em logs e prioritaria | **ELK** | Elasticsearch e superior para full-text |
| Volume extremo de logs (TB/dia) com restricao de custo | **LGTM** (Loki) | Loki e otimizado para custo em alto volume |
| Regulacao exige dados on-premises | **LGTM** ou **ELK** | SaaS pode nao atender compliance |
| Ja usa Prometheus e quer adicionar logs/traces | **LGTM** | Integracao natural com Grafana |
| Ja usa ELK e quer adicionar traces | **Elastic APM** ou **Jaeger** | Menos disruptivo que migrar stack completa |

---

## 10. Referencias

| Recurso | Link |
|---------|------|
| Grafana Labs - Documentacao | https://grafana.com/docs/ |
| Grafana - LGTM stack | https://grafana.com/about/grafana-stack/ |
| Prometheus - Documentacao | https://prometheus.io/docs/ |
| Loki - Documentacao | https://grafana.com/docs/loki/latest/ |
| Tempo - Documentacao | https://grafana.com/docs/tempo/latest/ |
| TraceQL - Referencia | https://grafana.com/docs/tempo/latest/traceql/ |
| grafana/otel-lgtm - Docker Hub | https://hub.docker.com/r/grafana/otel-lgtm |
| Mimir - Documentacao | https://grafana.com/docs/mimir/latest/ |
| Grafana Alerting | https://grafana.com/docs/grafana/latest/alerting/ |
