# 08 - Padroes Arquiteturais e Observabilidade

## 1. Observabilidade em Arquiteturas Event-Driven

### 1.1. Desafios

Arquiteturas event-driven introduzem complexidades que nao existem em chamadas sincronas:

| Desafio | Descricao |
|---|---|
| **Assincronia** | O produtor nao sabe quando (ou se) o consumidor processou a mensagem. Nao ha response HTTP para medir latencia end-to-end. |
| **Eventual Consistency** | O estado do sistema e consistente "eventualmente". Entre a producao e o consumo, dados podem parecer inconsistentes. |
| **Debug Distribuido** | Um pedido atravessa OrderService, Kafka, ProcessingWorker, Kafka novamente e NotificationWorker. Sem traces, identificar onde algo falhou e impraticavel. |
| **Fan-out/Fan-in** | Uma mensagem pode gerar multiplos eventos derivados. Rastrear a linhagem completa exige propagacao de contexto. |
| **Mensagens perdidas** | Sem metricas de consumer lag, mensagens podem ficar "presas" em topicos sem que ninguem perceba. |

### 1.2. Por Que Traces Sao Essenciais

Em um request sincrono (HTTP), o caller recebe um status code e latencia. Em pipelines event-driven, a unica forma de "ver" o caminho completo de um evento e atraves de **distributed tracing**:

```
OrderService         Kafka "orders"       ProcessingWorker       Kafka "notifications"    NotificationWorker
  [POST /orders] ──> [outbox CDC] ──────> [consume + process] ──> [produce] ────────────> [consume + persist]
       span 1             span 2                span 3                span 4                    span 5
       └──────────────────────────── trace_id: abc123 ──────────────────────────────────────────────┘
```

O trace unifica todos os spans sob um unico `trace_id`, permitindo:
- Medir **latencia end-to-end** (do POST ate a persistencia da notificacao)
- Identificar **gargalos** (qual span levou mais tempo)
- Detectar **falhas parciais** (trace incompleto indica que algum servico nao processou)

---

## 2. Transaction Outbox + CDC

### 2.1. O Problema: Dual-Write

Ao criar um pedido, o OrderService precisa:
1. Inserir o pedido no banco de dados
2. Publicar um evento no Kafka

Fazer ambos de forma independente gera o **problema do dual-write**:

```
# Cenario de falha:
INSERT INTO orders (...) -- sucesso
PUBLISH to Kafka         -- falha (Kafka indisponivel)
# Resultado: pedido existe no banco, mas nenhum evento foi emitido
```

A alternativa (publicar primeiro) tem o problema inverso. Nao ha como garantir atomicidade entre dois sistemas distintos sem um protocolo de consenso.

### 2.2. A Solucao: Outbox Pattern

O padrao Outbox resolve o dual-write usando uma **unica transacao** no banco:

```sql
BEGIN;
  INSERT INTO orders (id, description, status, ...) VALUES (...);
  INSERT INTO outbox_messages (id, aggregate_type, order_id, payload, traceparent, tracestate, ...)
    VALUES (...);
COMMIT;
```

Ambos os inserts estao na mesma transacao. Se qualquer um falhar, a transacao inteira e revertida. A consistencia e garantida pelo banco de dados.

### 2.3. Como a PoC Implementa

A PoC usa **Debezium CDC** (Change Data Capture) para ler a tabela outbox:

**Fluxo completo:**

```
OrderService                    PostgreSQL                     Debezium                  Kafka
    |                               |                              |                       |
    |-- BEGIN ------------------>   |                              |                       |
    |-- INSERT orders ---------->   |                              |                       |
    |-- INSERT outbox_messages ->   |                              |                       |
    |-- COMMIT ----------------->   |                              |                       |
    |                               |-- WAL (pgoutput) ----------> |                       |
    |                               |                              |-- EventRouter SMT --> |
    |                               |                              |   topic: "orders"     |
    |                               |                              |   headers:            |
    |                               |                              |     traceparent: ...  |
    |                               |                              |     tracestate: ...   |
```

**Componentes-chave:**

1. **PostgreSQL WAL (Write-Ahead Log):** configurado com `wal_level=logical`, permite que o Debezium leia mudancas como um subscriber de replicacao.

2. **Debezium PostgresConnector:** monitora a tabela `public.outbox_messages` e captura cada INSERT.

3. **EventRouter SMT (Single Message Transform):** transforma a mensagem do Debezium no formato desejado:
   - Extrai `payload` como valor da mensagem Kafka
   - Usa `order_id` como chave da mensagem
   - Roteia para o topico baseado em `aggregate_type` (mapeado para `orders`)
   - Propaga `traceparent` e `tracestate` como **headers Kafka**

**Configuracao do conector** (`ops/debezium/order-outbox-connector.json`):

```json
{
  "name": "order-outbox-connector",
  "config": {
    "connector.class": "io.debezium.connector.postgresql.PostgresConnector",
    "database.hostname": "postgres",
    "database.dbname": "otelpoc",
    "plugin.name": "pgoutput",
    "table.include.list": "public.outbox_messages",
    "transforms": "outbox",
    "transforms.outbox.type": "io.debezium.transforms.outbox.EventRouter",
    "transforms.outbox.table.field.event.key": "order_id",
    "transforms.outbox.table.field.event.payload": "payload",
    "transforms.outbox.route.topic.replacement": "orders",
    "transforms.outbox.table.fields.additional.placement":
      "traceparent:header:traceparent,tracestate:header:tracestate"
  }
}
```

### 2.4. Beneficios para Observabilidade

O campo `traceparent` na tabela outbox e o que torna o trace **end-to-end** possivel:

1. **OrderService** cria um span e serializa o `traceparent` W3C na coluna `outbox_messages.traceparent`
2. **Debezium** extrai o `traceparent` e o coloca como header Kafka (via EventRouter SMT)
3. **ProcessingWorker** le o header `traceparent` e cria um span filho, continuando o mesmo trace
4. A mensagem publicada no topico `notifications` tambem carrega o `traceparent`
5. **NotificationWorker** le o header e cria mais um span filho

Resultado: um **trace unico** que percorre OrderService -> Debezium/Kafka -> ProcessingWorker -> Kafka -> NotificationWorker.

---

## 3. Kafka Consumer Lag como Metrica de Saude

### 3.1. O Que E Consumer Lag

Consumer lag e a **diferenca** entre o offset mais recente do topico (last produced offset) e o offset do consumer group (last committed offset):

```
Topico "orders":  offset [0] [1] [2] [3] [4] [5] [6] [7] [8] [9]
                                                          ^         ^
                                              committed offset    latest offset
                                                    (6)              (9)

Consumer lag = 9 - 6 = 3 mensagens
```

### 3.2. Por Que E Uma Metrica Critica

| Situacao | Lag | Interpretacao |
|---|---|---|
| Operacao normal | 0-5 | Consumidor acompanha a producao |
| Pico temporario | 10-50 | Burst de producao, consumidor vai recuperar |
| Problema no consumidor | >100, crescendo | Consumidor parou ou esta muito lento |
| Consumidor offline | Crescente sem parar | Servico caiu ou nao esta deployado |

### 3.3. Como a PoC Mede

A PoC expoe a metrica como um **gauge** via instrumentacao customizada:

```
kafka_consumer_lag{service_name="processing-worker", topic="orders", consumer_group="..."}
kafka_consumer_lag{service_name="notification-worker", topic="notifications", consumer_group="..."}
```

O gauge e atualizado a cada ciclo de consumo, refletindo o lag atual.

### 3.4. Alerting Baseado em Lag

A regra `otel_poc_processing_lag_high` dispara quando o lag do ProcessingWorker no topico `orders` supera 100 por mais de 1 minuto:

```promql
sum by (topic, consumer_group) (kafka_consumer_lag{service_name="processing-worker",topic="orders"}) > 100
```

**Quando investigar:**
- Lag alto + throughput normal = consumidor nao esta processando
- Lag alto + throughput alto = pico de producao (pode se resolver sozinho)
- Lag crescente linear = consumidor offline ou erro sistematico

### 3.5. Lag vs. Throughput: Como Interpretar

Analise combinada dos paineis de throughput e lag:

| Throughput | Lag | Diagnostico |
|---|---|---|
| Normal | Baixo | Sistema saudavel |
| Alto | Crescente | Burst de producao, monitorar se recupera |
| Zero | Crescente | Consumidor parado -- acao necessaria |
| Normal | Estavel alto | Consumidor lento -- investigar processamento |
| Zero | Estavel | Sem producao, tudo parado (pode ser esperado) |

---

## 4. Correlation IDs em Event-Driven Architecture

### 4.1. trace_id como Correlation ID Natural

Em arquiteturas event-driven, e comum implementar um "correlation ID" customizado propagado manualmente. Com OpenTelemetry, o `trace_id` ja cumpre esse papel naturalmente:

- E gerado automaticamente no primeiro span
- Propaga-se via headers padrao W3C (`traceparent`)
- E indexado pelo Tempo para busca rapida
- Aparece automaticamente nos logs (via instrumentacao OTel)

Nao e necessario criar campos customizados como `correlationId` ou `requestId` -- o `trace_id` do OpenTelemetry ja e o identificador universal.

### 4.2. Persistencia do trace_id

O NotificationWorker persiste o `trace_id` na tabela de notificacoes. Isso permite:

- **Busca reversa:** dado um ID de notificacao no banco, encontrar o trace completo
- **Auditoria:** provar que uma notificacao especifica foi gerada a partir de um pedido especifico
- **Debug pos-fato:** semanas depois, ainda e possivel rastrear a linhagem completa

### 4.3. Busca Cross-Signal

Com o `trace_id` como elo de ligacao, a investigacao cross-signal se torna fluida:

```
Cenario: notificacao duplicada encontrada no banco

1. SELECT trace_id FROM notifications WHERE order_id = '...'
   -> trace_id = "abc123def456..."

2. Tempo: buscar trace "abc123def456..."
   -> ver todos os spans: OrderService, ProcessingWorker, NotificationWorker
   -> identificar se houve re-processamento (spans duplicados)

3. Loki: {service_name="notification-worker"} |= "abc123def456"
   -> ver logs do momento exato do processamento
   -> identificar erro ou retry que causou duplicacao

4. Prometheus: exemplar no grafico de latencia com trace_id "abc123def456"
   -> ver se houve spike de latencia no momento do evento
```

---

## 5. Health Checks e Readiness

### 5.1. Liveness vs. Readiness vs. Startup

| Probe | Pergunta | Falha Significa |
|---|---|---|
| **Liveness** | "O processo esta vivo?" | Container sera reiniciado |
| **Readiness** | "Pode receber trafico?" | Removido do load balancer |
| **Startup** | "Ja iniciou?" | Liveness nao avalia ate startup passar |

### 5.2. Como a PoC Trata Health Checks no Sampling

Health checks geram volume significativo de traces sem valor diagnostico. A PoC configura uma policy `drop-health-checks` no OTel Collector que descarta traces originados de endpoints como `/health`, `/ready` ou `/live`.

Isso e implementado no pipeline de traces do Collector, tipicamente com um `filter` processor:

```yaml
processors:
  filter/drop-health-checks:
    traces:
      span:
        - 'attributes["http.route"] == "/health"'
        - 'attributes["url.path"] == "/health"'
```

### 5.3. Por Que Nao Poluir Traces

- Health checks executam a cada poucos segundos por container
- Em 3 servicos com probes a cada 10s, sao **~26.000 traces/dia** sem valor
- Poluem o Tempo, aumentam custos de storage e dificultam buscas
- Distorcem metricas derivadas de traces (ex: latencia media cai artificialmente)

---

## 6. Observabilidade como Codigo (OaC)

### 6.1. O Que E Versionado na PoC

| Artefato | Caminho | Formato |
|---|---|---|
| Dashboard | `infra/grafana/dashboards/otel-poc-overview.json` | JSON |
| Dashboard provider | `infra/grafana/provisioning/dashboards/otel-poc-dashboards.yaml` | YAML |
| Datasource + exemplars | `infra/grafana/provisioning/datasources/otel-poc-datasource-exemplars.yaml` | YAML |
| Alert rules | `infra/grafana/provisioning/alerting/otel-poc-alert-rules.yaml` | YAML |
| Contact points | `infra/grafana/provisioning/alerting/otel-poc-contact-points.yaml` | YAML |
| Notification policies | `infra/grafana/provisioning/alerting/otel-poc-notification-policies.yaml` | YAML |
| OTel Collector config | Configuracao do `otelcol` no compose | YAML |
| Debezium connector | `ops/debezium/order-outbox-connector.json` | JSON |

### 6.2. Beneficios

- **Reproducibilidade:** `docker compose up` cria um ambiente completo com dashboards, alertas, datasources e conectores pre-configurados
- **Code Review:** mudancas em alertas passam por PR, com revisao tecnica antes de entrar em vigor
- **Rollback:** um alerta incorreto pode ser revertido com `git revert` em vez de navegacao manual no Grafana
- **Auditoria:** `git log` mostra quem alterou cada threshold, quando e por que
- **Onboarding:** novos desenvolvedores entendem a estrategia de observabilidade lendo os arquivos versionados

### 6.3. Anti-Padroes a Evitar

| Anti-Padrao | Problema |
|---|---|
| Dashboards criados apenas via UI | Perda ao recriar ambiente, sem review |
| Alertas sem `for` duration | Falsos positivos em picos transitorios |
| Thresholds hardcoded sem documentacao | Ninguem sabe por que o valor foi escolhido |
| Configuracao manual do Collector | Inconsistencia entre ambientes |

---

## 7. Referencias

- [Microservices Patterns - Chris Richardson](https://microservices.io/patterns/)
- [Transactional Outbox Pattern](https://microservices.io/patterns/data/transactional-outbox.html)
- [CDC com Debezium](https://debezium.io/documentation/reference/stable/connectors/postgresql.html)
- [Debezium Outbox Event Router](https://debezium.io/documentation/reference/stable/transformations/outbox-event-router.html)
- [Martin Fowler - Event-Driven Architecture](https://martinfowler.com/articles/201701-event-driven.html)
- [W3C Trace Context](https://www.w3.org/TR/trace-context/)
- [OpenTelemetry Context Propagation](https://opentelemetry.io/docs/concepts/context-propagation/)
- [Kafka Consumer Lag Monitoring](https://www.confluent.io/blog/kafka-lag-monitoring-and-metrics-at-applegreen/)
