# Roadmap

**Current Milestone:** M4 — Hardening e Documentação da PoC
**Status:** Completed

---

## M1 — Infraestrutura e Esqueleto dos Serviços

**Goal:** Ambiente Docker funcional com todos os serviços inicializando sem erro, conectados ao OTel Collector e ao backend LGTM. Traces básicos visíveis no Tempo.

**Target:** Todos os containers sobem com `docker-compose up -d` e traces de health/startup aparecem no Grafana Tempo.

### Features

**Infraestrutura Docker Compose** — DONE

- Estender `docker-compose.yaml` do `otel-demo-main` com Kafka + Zookeeper + PostgreSQL
- Adicionar os 3 serviços .NET ao compose (build de imagem local)
- Configurar variáveis de ambiente para OTLP endpoint, Kafka brokers e connection string Postgres
- Validar que todos os containers sobem sem erro e se comunicam

Status atual: `docker-compose.yaml` agora inclui `order-service`, `processing-worker` e `notification-worker` com build pela raiz do repositório, variáveis de ambiente de OTLP/Kafka/Postgres e `depends_on` com `condition: service_healthy` para Kafka e Postgres. `docker compose config`, `docker compose build`, `docker compose up -d`, `docker compose ps` e os logs iniciais dos 3 serviços passaram. O próximo passo de M1 é instrumentar os serviços com OpenTelemetry na feature `otel-bootstrap`.

**Solução .NET** — DONE

- Criar solution `otel-poc.sln` com 3 projetos: `OrderService`, `ProcessingWorker`, `NotificationWorker`
- Configurar `Directory.Build.props` com versão do SDK e pacotes comuns de OTel
- Criar `Dockerfile` multi-stage para cada serviço

Status atual: `otel-poc.sln`, `global.json`, `Directory.Build.props`, os 3 projetos em `src/` e os 3 Dockerfiles multi-stage foram criados. `dotnet build otel-poc.sln` passou em container com SDK 10 e os 3 `docker build` passaram localmente. O host Windows segue sem SDK 10 instalado, então o build direto com `dotnet` local permanece bloqueado por ambiente.

**Instrumentação Base (OpenTelemetry Bootstrap)** — DONE

- Adicionar e configurar `OpenTelemetry.Extensions.Hosting` nos 3 projetos
- Configurar OTLP exporter apontando para o `otelcol` do `otel-demo-main`
- Habilitar `OpenTelemetry.Instrumentation.AspNetCore` no OrderService
- Habilitar `OpenTelemetry.Instrumentation.Http` nos 3 serviços
- Configurar Resource com `service.name` e `service.version`
- Validar: 3 serviços visíveis como sources distintos no Tempo

Status atual: `AddOtelInstrumentation()` foi implementado nos 3 serviços e os `Program.cs` foram reduzidos a uma chamada única de bootstrap. O build da solution passou com SDK 10 em container, o `otelcol` voltou a subir após remover a chave incompatível `file_format` de `otelcol.yaml`, o processor `drop-health-checks` foi ajustado para descartar 100% dos health checks bem-sucedidos e os dois workers passaram a emitir spans manuais de heartbeat para comprovação de `service.name` em M1. A validação no Tempo confirmou traces recentes para `order-service`, `processing-worker` e `notification-worker`, além de ausência de traces recentes para `/health`.

---

## M2 — Fluxo de Eventos End-to-End

**Goal:** Request no OrderService gera um trace distribuído completo, atravessando Kafka e os 2 workers, com trace único conectado no Tempo.

**Target:** Uma chamada `POST /orders` resulta em trace com todos os spans de OrderService → ProcessingWorker → NotificationWorker linkados por TraceId.

### Features

**OrderService — API e Persistência** — DONE

- Implementar `POST /orders` com payload mock (OrderId + Timestamp)
- Implementar `GET /orders/{id}` retornando dados do PostgreSQL
- Configurar `Npgsql.EntityFrameworkCore.PostgreSQL` + `OpenTelemetry.Instrumentation.EntityFrameworkCore`
- Publicar mensagem no topic Kafka `orders` com trace context propagado via headers
- Logs estruturados com TraceId/SpanId no contexto

Status atual: `POST /orders` e `GET /orders/{id}` foram implementados em Minimal API com persistência PostgreSQL via EF Core, estados `pending_publish` / `published` / `publish_failed`, publicação Kafka no topic `orders` com header `traceparent`, spans de EF Core e span manual `kafka publish orders`. A validação passou com `docker compose up -d --build`, `POST` e `GET` reais, inspeção direta da tabela `orders`, captura do evento Kafka com header W3C e consulta no Tempo confirmando o trace HTTP -> DB -> Kafka. O caminho de falha também foi validado com Kafka indisponível, retornando `503` e persistindo `publish_failed`.

**ProcessingWorker — Consumer + HTTP Call** — DONE

- Implementar consumer do topic `orders`
- Extrair trace context dos headers Kafka e criar span filho
- Realizar HTTP GET para `OrderService /orders/{id}` (span de saída rastreado)
- Publicar mensagem no topic `notifications` com trace context propagado
- Logs estruturados com correlação ao trace

Status atual: o `ProcessingWorker` agora consome o topic `orders`, extrai `traceparent` e `tracestate` quando presentes, inicia o span manual `kafka consume orders`, chama `GET /orders/{id}` com `HttpClient` instrumentado e publica payload enriquecido mínimo em `notifications` com propagação manual W3C preservada. A implementação também trata `404`, `5xx`, timeout, falha de rede, payload inválido e headers Kafka ausentes ou inválidos sem derrubar o host nem publicar mensagens indevidas. A validação local passou com `docker compose up -d --build`, build da solution em container SDK 10, inspeção direta do topic `notifications` e consultas no Tempo confirmando o caminho feliz `POST /orders` -> `kafka publish orders` -> `kafka consume orders` -> `GET` -> `kafka publish notifications`, além dos caminhos de `404`, `5xx`, timeout e mensagem sem headers W3C. O próximo passo natural de M2 é especificar e implementar a feature `NotificationWorker — Consumer + Persistência`.

**NotificationWorker — Consumer + Persistência** — DONE

- Implementar consumer do topic `notifications`
- Extrair trace context dos headers Kafka e criar span filho
- Persistir resultado (mock) no PostgreSQL com trace de DB
- Logs estruturados com correlação ao trace

Status atual: o `NotificationWorker` agora consome o topic `notifications`, extrai `traceparent` e `tracestate` quando validos, inicia o span manual `kafka consume notifications`, valida o payload minimo e persiste o resultado em `notification_results` com `persistedAtUtc` e `traceId` sem alterar o contrato do evento. A implementacao tambem classifica `consume_failed`, `invalid_payload` e `persistence_failed` sem derrubar o host, usando error handler dedicado do consumer Kafka e spans de erro observaveis. A validacao passou com build do projeto e da solution em container SDK 10, `docker compose up -d --build`, consulta direta ao PostgreSQL, injecao manual de payload invalido e falha de persistencia com PostgreSQL parado, alem de consultas ao Tempo confirmando o trace feliz `POST /orders` -> `kafka publish orders` -> `kafka consume orders` -> `GET` -> `kafka publish notifications` -> `kafka consume notifications` -> span DB e os traces de erro para `invalid_payload` e `persistence_failed`. O proximo passo natural e especificar a feature `Propagação de Trace Context no Kafka` para remover a duplicacao dos helpers W3C entre os servicos.

**Propagacao de Trace Context no Kafka** — DONE

- Consolidar a logica W3C em um helper compartilhado entre os 3 servicos, reduzindo a duplicacao atual de `KafkaTracingHelper`
- Padronizar o contrato minimo `Extract(Headers?)` e `Inject(Activity?, Headers)` sem alterar payloads, topicos ou nomes de spans
- Migrar de forma cirurgica, preservando a baseline validada de M2 para fluxo feliz e caminhos degradados
- Revalidar o trace distribuido completo no Tempo e a presenca dos headers W3C nos topicos `orders` e `notifications`

Status atual: a logica W3C de `traceparent` e `tracestate` foi consolidada em `src/Shared/W3CTraceContext.cs`, compartilhada pelos tres servicos via inclusao cirurgica nos `.csproj`, enquanto os `KafkaTracingHelper` locais foram reduzidos a fachadas finas com contrato uniforme `Extract(Headers?)` e `Inject(Activity?, Headers)`. A validacao passou com build da solution em container SDK 10, `docker compose up -d --build`, confirmacao de `traceparent` nos topics `orders` e `notifications`, trace feliz no Tempo com `POST /orders` -> `kafka publish orders` -> `kafka consume orders` -> `GET /orders/{id}` -> `kafka publish notifications` -> `kafka consume notifications` -> span DB e cenarios degradados com headers ausentes iniciando novo trace local no `ProcessingWorker` e no `NotificationWorker`. Com isso, M2 fica fechado e o proximo passo natural e especificar a feature `Metricas Customizadas` de M3.

---

## M3 — Métricas e Observabilidade Avançada

**Goal:** Dashboard Grafana funcional mostrando RED metrics dos 3 serviços, Kafka consumer lag e métricas de banco de dados. Pelo menos 1 alerta ativo.

**Target:** Dashboard com painéis funcionais + alerta disparando em simulação de carga.

### Features

**Metricas Customizadas** - DONE

- `OrderService`: `orders.created.total` (counter), `orders.create.duration` (histogram), `orders.backlog.current` (gauge)
- `ProcessingWorker`: `orders.processed.total` (counter), `orders.processing.duration` (histogram), `kafka.consumer.lag` (gauge)
- `NotificationWorker`: `notifications.persisted.total` (counter), `notifications.persistence.duration` (histogram), `kafka.consumer.lag` (gauge)
- Exportacao continua via OTLP gRPC -> `otelcol:4317` -> pipeline `metrics` do collector -> LGTM/Prometheus ja existente

Status atual: a feature `metricas-customizadas` foi implementada nos tres servicos com `WithMetrics(...)` no bootstrap OTel, recorders locais (`OrderMetrics`, `ProcessingMetrics`, `NotificationMetrics`), gauges por snapshot para `orders.backlog.current` e `kafka.consumer.lag` e integracao direta dos counters/histograms aos fluxos reais de `POST /orders`, processamento no `ProcessingWorker` e persistencia no `NotificationWorker`. A validacao passou com build da solution em container SDK 10, `docker compose up -d --build`, fluxo feliz ponta a ponta sem regressao funcional e series consultaveis no LGTM/Prometheus, incluindo as formas normalizadas `orders_created_total`, `orders_backlog_current`, `orders_processed_total`, `notifications_persisted_total`, `kafka_consumer_lag` e os histograms `orders_create_duration_milliseconds_*`, `orders_processing_duration_milliseconds_*` e `notifications_persistence_duration_milliseconds_*`. Tambem foi validada a ausencia de labels customizadas de alta cardinalidade como `orderId`, `traceId`, `spanId`, `description` ou payload bruto. O proximo passo natural e especificar a feature `Dashboard Grafana`.

**Dashboard Grafana** — DONE

- Provisionar dashboard via arquivo JSON versionado e carregado pelo Grafana do stack LGTM existente, sem alterar a exportacao OTLP atual
- Painel minimo do `OrderService`: throughput de criacao, latencia P50/P95 e backlog atual por status agregado
- Painel minimo do `ProcessingWorker`: throughput de processamento, latencia P50/P95 e `kafka_consumer_lag` do topic `orders`
- Painel minimo do `NotificationWorker`: throughput de persistencia, latencia P50/P95 e `kafka_consumer_lag` do topic `notifications`
- Queries baseadas nas series normalizadas ja validadas no backend: `orders_created_total`, `orders_backlog_current`, `orders_processed_total`, `notifications_persisted_total`, `kafka_consumer_lag`, `orders_create_duration_milliseconds_*`, `orders_processing_duration_milliseconds_*` e `notifications_persistence_duration_milliseconds_*`
- Escopo explicitamente separado da feature posterior `Alertas Grafana`, sem criar regras, contact points ou novas metricas nesta etapa

Status atual: a feature `dashboard-grafana` foi implementada e validada com provider proprio da PoC, dashboard JSON versionado de 9 paineis, mounts read-only no `lgtm` para `/otel-lgtm/grafana/conf/provisioning/dashboards` e `/otel-lgtm/dashboards`, binding explicito ao datasource `uid: prometheus` e queries PromQL normalizadas `*_duration_milliseconds_*` renderizando no Grafana. Com a baseline visual de M3 consolidada, o proximo passo natural e especificar e depois implementar a feature `alertas-grafana` sem reabrir escopo de metricas, collector ou servicos .NET.

**Alertas Grafana** — DONE

- Alerta 1: Latência P95 do `OrderService` > 500ms por 1 minuto
- Alerta 2: `ProcessingWorker` consumer lag > 100 mensagens
- Configurar contact point local (log/webhook mock ou equivalente simples) no Grafana

Status atual: a feature `alertas-grafana` foi implementada com artefatos versionados em `infra/grafana/provisioning/alerting`, mounts read-only no `lgtm` para `/otel-lgtm/grafana/conf/provisioning/alerting`, contact point local via `ops/alert-webhook-mock` e policy minima unica apontando para ele. A validacao confirmou o datasource nativo em `/otel-lgtm/grafana/conf/provisioning/datasources/grafana-datasources.yaml` com `uid: prometheus`, o auto-provisionamento por arquivo das duas regras obrigatorias e os caminhos reais do runtime Grafana 12.4.1. O alerta `ProcessingWorker lag > 100` foi validado ponta a ponta em `Pending`, `Firing` e `Resolved` com payloads recebidos no receiver local, enquanto o alerta `OrderService P95 > 500 ms` foi validado por runtime real com requests acima de 500 ms, regra em `Firing` e payload correspondente no webhook mock, sem reabrir escopo de metricas, collector, pipelines OTLP ou servicos .NET. Com isso, M3 fica fechado e o proximo passo natural e especificar a feature `README da PoC` de M4.

---

## M4 — Hardening e Documentação da PoC

**Goal:** PoC documentada, com guia de execução e demonstração do valor de cada pilar de observabilidade.

**Target:** README completo + `docker-compose up` funciona em ambiente limpo na primeira execução.

### Features

**README da PoC** — DONE

- Pré-requisitos e instruções de execução
- Guia de demonstração: passo a passo para gerar carga e observar nos dashboards
- Referência a cada pilar: traces, metrics, logs, alerts
- Matriz Host versus Rede Interna para clareza topológica
- Troubleshooting e artefatos de referência da baseline

Status atual: `README.md` foi criado com todas as seções obrigatórias, alinhado contra os artefatos versionados da baseline (`docker-compose.yaml`, `src/OrderService/Program.cs`, `infra/grafana/dashboards/otel-poc-overview.json`, `infra/grafana/provisioning/alerting/otel-poc-alert-rules.yaml`, `infra/grafana/provisioning/alerting/otel-poc-contact-points.yaml`, `infra/grafana/provisioning/alerting/otel-poc-notification-policies.yaml`, `ops/alert-webhook-mock/server.py`). Validação passou: dashboard UID `otel-poc-m3-overview` confirmado, alertas "OrderService P95 > 500 ms" e "ProcessingWorker lag > 100" presentes, endpoints `/orders`, `/orders/{id}` e `/health` documentados corretamente, matriz de rede completa e precisa.

**Gerador de Carga** — DONE

- Utilitario externo simples, preferencialmente host-side, que dispara N requests reais para `http://localhost:8080/orders`
- Cobrir um modo de fluxo feliz para popular traces, metricas, logs e dashboard sem alterar a aplicacao
- Cobrir um modo opcional de pressao de latencia para apoiar a demonstracao do alerta existente do `OrderService`

Status atual: feature `gerador-de-carga` foi implementada em `ops/load-generator/generate-orders.ps1` com spec, design e tasks concluídos. Script PowerShell host-side com validação de parâmetros, payload builder com descrições únicas, executor HTTP único contra `POST /orders`, modo happy para fluxo sequencial, modo latency para concorrência controlada, resumo consolidado com estatísticas de latência e exit codes semânticos. Validação passou com smoke tests: modo happy com 20 pedidos, modo latency com 10 pedidos e concorrência 2, validação de parâmetros inválidos com exit code 1, sucesso com exit code 0. README atualizado com referência minima ao gerador como helper externo de demonstração. Nenhuma alteração em `src/`, `docker-compose.yaml`, `infra/otel/otelcol.yaml`, `infra/otel/processors/` ou `infra/grafana/`.
- Complementar o `README.md` canonico sem criar um segundo roteiro de demo e sem exigir mudancas em compose, collector ou servicos .NET

Status atual: a feature foi especificada como o proximo passo de M4 com foco em utilitario minimo de demonstracao, reaproveitando o compose da raiz, o endpoint real do `OrderService` e os alertas Grafana ja provisionados.

**Exportação de Logs OTLP** — DONE

- Habilitar `builder.Logging.AddOpenTelemetry(...)` nos 3 serviços, reutilizando `otlpEndpoint` e `resourceBuilder` já existentes em cada `OtelExtensions.cs`
- Garantir que logs estruturados dos 3 serviços chegam ao OTel Collector e ao Loki, com campos `TraceId`, `SpanId` e `service_name`
- Corrigir o crash `InvalidOperationException: Nullable object must have a value` no `ProcessingWorker/Worker.cs` causado por pedidos com status `pending_publish` atingindo a linha `order.PublishedAtUtc!.Value` sem guarda de status
- Nenhum novo pacote NuGet necessário — `OpenTelemetry.Extensions.Hosting` já inclui suporte a logs via `services.AddLogging`
- `infra/otel/otelcol.yaml` já possui pipeline `logs` funcional apontando para Loki — nenhuma alteração necessária no collector

**Refactor do Design do Repositório** — DONE

- Reorganizar a estrutura para separar aplicação, infraestrutura observável e utilitários operacionais
- Manter `docker-compose.yaml`, `otel-poc.sln`, `Directory.Build.props`, `global.json` e `README.md` na raiz
- Mover Grafana, collector, processors e bootstrap SQL para `infra/`
- Mover webhook mock, Debezium e gerador de carga para `ops/`
- Atualizar compose, README e brownfield docs para a nova árvore

Status atual: o refactor conservador do design do repositório foi concluído sem mover `src/`. A nova baseline mantém código de aplicação em `src/`, infraestrutura em `infra/` e utilitários operacionais em `ops/`, reduzindo poluição da raiz e preservando o bootstrap pelo `docker-compose.yaml`. As referências de caminhos foram atualizadas no compose, no `README.md` e nos brownfield docs de `.specs/codebase`, sem reabrir escopo de `.sln`, `.csproj` ou Dockerfiles dos serviços.

Status atual: T1 (fix `HandleLookupOutcome` com guarda `!= "published"` antes do acesso a `PublishedAtUtc`) e T2/T3/T4 (`services.AddLogging(logging => logging.AddOpenTelemetry(...))` nos 3 `OtelExtensions.cs` com `IncludeFormattedMessage = true` e `IncludeScopes = true`) implementados. Build da solution em container SDK 10 passou com 0 erros e 0 warnings.

---

## Future Considerations

- Schema Registry + Avro para serialização de eventos Kafka
- Dead Letter Queue (DLQ) e alertas de mensagens mortas
- Exemplars linkando métricas Prometheus a traces Tempo
- Configurar tail sampling no OTel Collector específico para os serviços da PoC
- Multi-tenancy com `X-Scope-OrgID` (Grafana Cloud readiness)
- Health checks instrumentados e descartados pelo `drop-health-checks` existente
