# 09 - Guia Pratico

## 1. Pre-requisitos

| Requisito | Versao Minima | Observacao |
|---|---|---|
| **Docker Desktop** | 4.x com Compose v2 | `docker compose version` deve retornar v2.x |
| **.NET 8 SDK** | 8.0 | Opcional -- necessario apenas para desenvolvimento local fora do Docker |
| **PowerShell** | 5.1+ | Necessario para o script de geracao de carga |
| **RAM disponivel** | ~4 GB | Para todos os containers simultaneamente |
| **Portas livres** | 3000, 8080, 8083, 8085, 8888 | Verificar antes de subir o compose |

Para verificar pre-requisitos:

```bash
docker compose version    # Docker Compose v2.x.x
dotnet --version          # 8.0.x (opcional)
pwsh --version            # PowerShell 7.x (ou powershell no Windows)
```

---

## 2. Quick Start

### Passo 1: Subir a infraestrutura

```bash
docker compose up -d
```

Aguardar aproximadamente 30 segundos para que todos os servicos estabilizem (especialmente Kafka e PostgreSQL).

### Passo 2: Verificar se os servicos estao saudaveis

```bash
docker compose ps
```

Todos os containers devem estar com status `Up` ou `healthy`.

### Passo 3: Registrar o conector Debezium

```bash
curl -X POST http://localhost:8083/connectors \
  -H "Content-Type: application/json" \
  -d @ops/debezium/order-outbox-connector.json
```

Verificar se o conector foi criado:

```bash
curl http://localhost:8083/connectors
# Resposta esperada: ["order-outbox-connector"]
```

### Passo 4: Criar um pedido de teste

```bash
curl -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -d '{"description":"Meu primeiro pedido"}'
```

Resposta esperada (exemplo):

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "description": "Meu primeiro pedido",
  "status": "Created"
}
```

### Passo 5: Verificar o pipeline completo

1. Abrir o Grafana em `http://localhost:3000`
2. Navegar ate **Explore > Tempo**
3. Buscar traces recentes -- deve haver um trace com spans do OrderService, ProcessingWorker e NotificationWorker

---

## 3. Acessando as Ferramentas

| Ferramenta | URL | Descricao |
|---|---|---|
| **Grafana** | [http://localhost:3000](http://localhost:3000) | Dashboards, alertas, explorar metricas/traces/logs |
| **Kafka UI** | [http://localhost:8085](http://localhost:8085) | Visualizar topicos, mensagens, consumer groups |
| **OrderService API** | [http://localhost:8080](http://localhost:8080) | API HTTP para criacao de pedidos (`POST /orders`) |
| **Kafka Connect API** | [http://localhost:8083](http://localhost:8083) | Gerenciar conectores Debezium |
| **OTel Collector Metrics** | [http://localhost:8888/metrics](http://localhost:8888/metrics) | Metricas internas do Collector (Prometheus format) |

---

## 4. Gerando Carga

### 4.1. Script de Geracao de Carga

O script `ops/load-generator/generate-orders.ps1` automatiza a criacao de pedidos em massa.

**Parametros:**

| Parametro | Padrao | Descricao |
|---|---|---|
| `-Count` | (obrigatorio) | Numero total de pedidos a gerar |
| `-Mode` | `happy` | Modo: `happy` (sequencial) ou `latency` (concorrente) |
| `-Concurrency` | `1` | Numero de requests concorrentes (apenas modo `latency`) |
| `-BaseUrl` | `http://localhost:8080` | URL base do OrderService |
| `-TimeoutSeconds` | `10` | Timeout HTTP por request |
| `-PauseMs` | `0` | Pausa entre requests/batches em milissegundos |

### 4.2. Exemplos de Uso

**Geracao basica (20 pedidos sequenciais):**

```powershell
.\ops\load-generator\generate-orders.ps1 -Count 20
```

**Geracao com pressao de latencia (120 pedidos, 6 concorrentes):**

```powershell
.\ops\load-generator\generate-orders.ps1 -Count 120 -Mode latency -Concurrency 6
```

**Geracao com pausa entre batches:**

```powershell
.\ops\load-generator\generate-orders.ps1 -Count 50 -Mode latency -Concurrency 5 -PauseMs 200
```

O script exibe progresso em tempo real e um sumario final com estatisticas (total, sucesso, falhas, latencia min/avg/max).

### 4.3. Teste Rapido com curl

Para testes manuais pontuais:

```bash
# Criar um pedido
curl -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -d '{"description":"pedido-teste-manual"}'

# Criar varios pedidos em sequencia (bash)
for i in $(seq 1 10); do
  curl -s -X POST http://localhost:8080/orders \
    -H "Content-Type: application/json" \
    -d "{\"description\":\"pedido-$i\"}"
  echo ""
done
```

---

## 5. Explorando Dados no Grafana

### 5.1. Metricas (Prometheus)

1. Abrir **Explore** (icone de bussola no menu lateral)
2. Selecionar datasource **Prometheus**
3. Digitar uma query PromQL, por exemplo:

```promql
# Taxa de criacao de pedidos por segundo
sum by (result) (rate(orders_created_total{service_name="order-service"}[5m]))

# Latencia P95 do OrderService
histogram_quantile(0.95, sum by (le) (rate(orders_create_duration_milliseconds_bucket{service_name="order-service"}[5m])))

# Consumer lag do ProcessingWorker
kafka_consumer_lag{service_name="processing-worker"}

# Backlog de mensagens outbox
orders_backlog_current{service_name="order-service"}
```

### 5.2. Traces (Tempo)

1. Abrir **Explore**
2. Selecionar datasource **Tempo**
3. Buscar por:
   - **Trace ID:** colar um trace_id especifico
   - **TraceQL:** linguagem de busca do Tempo

Exemplos de TraceQL:

```
# Traces do OrderService com latencia > 200ms
{resource.service.name="order-service" && span.http.method="POST"} | duration > 200ms

# Traces com erro
{status = error}

# Traces do pipeline completo
{resource.service.name="order-service"}
```

### 5.3. Logs (Loki)

1. Abrir **Explore**
2. Selecionar datasource **Loki**
3. Usar LogQL:

```logql
# Logs do OrderService
{service_name="order-service"}

# Logs com nivel de erro
{service_name="processing-worker"} |= "error"

# Logs de um trace especifico
{service_name=~"order-service|processing-worker|notification-worker"} |= "abc123def456"
```

### 5.4. Dashboard da PoC

1. No menu lateral, clicar em **Dashboards**
2. Abrir a pasta **OTel PoC**
3. Clicar em **OTel PoC - Service Metrics**

O dashboard exibe 10 panels com metricas dos 3 servicos: throughput, latencia P50/P95, consumer lag, backlog e exemplars.

---

## 6. Verificando Alertas

### 6.1. Disparando um Alerta de Latencia

Para triggerar a regra "OrderService P95 > 500ms", gere carga concorrente suficiente para elevar a latencia:

```powershell
.\ops\load-generator\generate-orders.ps1 -Count 200 -Mode latency -Concurrency 10
```

### 6.2. Verificando no Grafana

1. Navegar ate **Alerting > Alert rules** no menu lateral
2. Localizar a regra "OrderService P95 > 500 ms"
3. Os estados possiveis sao:
   - **Normal:** condicao nao atendida
   - **Pending:** condicao atendida, aguardando `for` duration (1 minuto)
   - **Firing:** condicao persistiu por 1 minuto, notificacao enviada

### 6.3. Verificando o Webhook Mock

O `alert-webhook-mock` registra todas as notificacoes recebidas. Para inspecionar:

```bash
# Listar notificacoes recebidas
curl http://localhost:8080/requests
```

> Nota: a porta do webhook mock pode variar dependendo da configuracao do Docker Compose. Verifique o mapeamento de portas com `docker compose ps alert-webhook-mock`.

### 6.4. Regras de Alerta Configuradas

| Regra | Metrica | Threshold | For | Severity |
|---|---|---|---|---|
| OrderService P95 > 500 ms | `histogram_quantile(0.95, ...)` | > 500 ms | 1m | warning |
| ProcessingWorker lag > 100 | `kafka_consumer_lag{...}` | > 100 | 1m | warning |

---

## 7. Checklist de Observabilidade para Novos Servicos

Ao adicionar um novo servico ao ecossistema, seguir este checklist:

- [ ] **Pacotes OTel NuGet:** adicionar `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol` e instrumentacoes relevantes (AspNetCore, HttpClient, etc.)
- [ ] **Configurar OTel Extensions:** registrar TracerProvider, MeterProvider e LoggerProvider com exportador OTLP apontando para o Collector
- [ ] **Definir metricas customizadas:** criar Counter (throughput), Histogram (latencia) e Gauge (estado atual) conforme necessidade do servico
- [ ] **Propagar trace context em mensageria:** ao produzir/consumir mensagens Kafka, injetar/extrair headers `traceparent` e `tracestate`
- [ ] **Criar/atualizar dashboard Grafana:** adicionar panels no JSON e versionar em `infra/grafana/dashboards/`
- [ ] **Definir alert rules para SLIs:** criar regras baseadas nos indicadores mais criticos do servico (latencia, error rate, lag)
- [ ] **Habilitar exemplars:** configurar histogramas para emitir exemplars com `trace_id`
- [ ] **Testar correlacao logs-traces-metricas:** verificar que um `trace_id` aparece nos 3 sinais e que a navegacao no Grafana funciona end-to-end

---

## 8. Troubleshooting

### Servico nao aparece no Grafana

**Sintoma:** metricas, traces ou logs do servico nao aparecem no Grafana.

**Verificar:**
1. A variavel de ambiente `OTEL_EXPORTER_OTLP_ENDPOINT` aponta para o Collector (`http://otelcol:4317` ou `http://otelcol:4318`)
2. O container do servico consegue resolver o DNS do Collector (`docker compose exec <servico> ping otelcol`)
3. O Collector esta rodando: `docker compose logs otelcol`

### Traces incompletos

**Sintoma:** trace mostra spans de um servico mas nao do proximo na pipeline.

**Verificar:**
1. O produtor esta injetando `traceparent` nos headers Kafka
2. O consumidor esta extraindo `traceparent` dos headers e criando span filho
3. O Debezium connector esta propagando headers (campo `table.fields.additional.placement` no connector config)

### Metricas zeradas

**Sintoma:** metricas existem no Prometheus mas sempre retornam 0.

**Verificar:**
1. O `Meter` name no codigo corresponde ao registrado no MeterProvider
2. O servico esta efetivamente processando requests (verificar logs)
3. O time range no Grafana cobre o periodo de atividade

### Kafka lag crescendo indefinidamente

**Sintoma:** metrica `kafka_consumer_lag` cresce continuamente.

**Verificar:**
1. O consumer esta rodando: `docker compose logs <worker>`
2. Nao ha excecoes no processamento: buscar erros nos logs
3. O consumer group esta registrado: verificar no Kafka UI (`http://localhost:8085`)
4. O topico tem particoes atribuidas ao consumer

### Dashboard vazio

**Sintoma:** todos os panels do dashboard mostram "No data".

**Verificar:**
1. **Time range:** ajustar para `Last 30 minutes` ou `Last 1 hour`
2. **Datasource:** confirmar que o Prometheus esta acessivel (Explore > Prometheus > digitar `up`)
3. **Dados existem:** executar a query manualmente no Explore
4. **Refresh:** clicar no botao de refresh ou verificar se auto-refresh esta ativo

### Alertas nao disparando

**Sintoma:** a condicao do alerta e atendida, mas nenhuma notificacao e enviada.

**Verificar:**
1. **Evaluation interval:** a regra avalia a cada 30s; aguardar pelo menos 2 minutos
2. **For duration:** a condicao deve persistir por `1m` antes de disparar
3. **Estado do alerta:** verificar em Alerting > Alert rules se esta em `Pending` ou `Normal`
4. **Contact point:** verificar se o webhook mock esta rodando (`docker compose ps alert-webhook-mock`)
5. **Notification policy:** confirmar que o receiver correto esta configurado

### Debezium nao captura eventos

**Sintoma:** mensagens sao inseridas em `outbox_messages` mas nao aparecem no topico Kafka.

**Verificar:**
1. O conector esta registrado: `curl http://localhost:8083/connectors`
2. O conector nao esta em estado `FAILED`: `curl http://localhost:8083/connectors/order-outbox-connector/status`
3. O PostgreSQL tem `wal_level=logical`: `docker compose exec postgres psql -U poc -d otelpoc -c "SHOW wal_level;"`
4. A tabela `outbox_messages` esta na lista de inclusao do conector

---

## 9. Referencias

- [Repositorio da PoC (README)](../README.md)
- [Docker Compose Documentation](https://docs.docker.com/compose/)
- [Debezium PostgreSQL Connector](https://debezium.io/documentation/reference/stable/connectors/postgresql.html)
- [Grafana Explore](https://grafana.com/docs/grafana/latest/explore/)
- [PromQL Documentation](https://prometheus.io/docs/prometheus/latest/querying/basics/)
- [TraceQL Documentation](https://grafana.com/docs/tempo/latest/traceql/)
- [LogQL Documentation](https://grafana.com/docs/loki/latest/logql/)
