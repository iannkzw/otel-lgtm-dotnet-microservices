# Metricas Customizadas - Tasks

**Design**: `.specs/features/metricas-customizadas/design.md`
**Status**: Tasks Defined

---

## Execution Plan

### Phase 1: OrderService

```text
T1 (recorder OrderService) -> T2 (snapshot backlog) -> T3 (bootstrap metrics) -> T4 (emissao no endpoint)
```

### Phase 2: Workers

```text
T5 (recorder ProcessingWorker) -> T6 (snapshot lag ProcessingWorker) -> T7 (bootstrap metrics ProcessingWorker) -> T8 (emissao no worker)
T9 (recorder NotificationWorker) -> T10 (snapshot lag NotificationWorker) -> T11 (bootstrap metrics NotificationWorker) -> T12 (emissao no worker)
```

### Phase 3: Validacao integrada

```text
T4 + T8 + T12 -> T13 (build + smoke tests sem regressao) -> T14 (validacao LGTM/Prometheus)
```

---

## Task Breakdown

### T1: Criar o recorder de metricas do OrderService

**What**: Introduzir o recorder local do `OrderService` para centralizar os instrumentos `orders.created.total` e `orders.create.duration`, os resultados canonicos da feature e a fronteira unica de gravacao para o endpoint `POST /orders`.

**Where**: `src/OrderService/Metrics/`

**Depends on**: feature `propagacao-trace-context-kafka` concluida

**Done when**:

- [ ] Existe `OrderMetrics` com `Meter` proprio do servico
- [ ] Os nomes canonicos `orders.created.total` e `orders.create.duration` estao definidos em um unico ponto
- [ ] Os resultados permitidos sao apenas `created`, `validation_failed`, `persist_failed`, `publish_failed` e `status_update_failed`
- [ ] O contrato do recorder permite registrar counter e histogram sem espalhar strings de metrica pelo endpoint
- [ ] Nao existe dependencia nova de biblioteca compartilhada de metricas fora do `OrderService`

**Verification**:

- Local: build de `src/OrderService/OrderService.csproj` passa via SDK 10 em container
- Backend: nao aplicavel nesta tarefa

---

### T2: Criar snapshot e sampler do backlog no OrderService

**What**: Introduzir o estado em memoria e o refresh bounded do gauge `orders.backlog.current`, mantendo a leitura do banco fora do callback observavel.

**Where**: `src/OrderService/Metrics/`, `src/OrderService/Data/`

**Depends on**: T1

**Done when**:

- [ ] Existe `OrderBacklogSnapshot` singleton com contadores separados para `pending_publish` e `publish_failed`
- [ ] Existe `OrderBacklogSampler` ou hosted service equivalente atualizando o snapshot em intervalo fixo
- [ ] A consulta de backlog e agregada por status e nao itera pedido por pedido
- [ ] Falhas do sampler geram apenas log diagnostico sucinto e preservam ultimo snapshot conhecido ou fallback seguro
- [ ] O callback futuro do gauge depende apenas do snapshot em memoria

**Verification**:

- Local: build de `src/OrderService/OrderService.csproj` passa via SDK 10 em container
- Backend: nao aplicavel nesta tarefa

---

### T3: Ligar o bootstrap de metricas do OrderService ao pipeline OTel existente

**What**: Estender o bootstrap OpenTelemetry do `OrderService` para registrar `WithMetrics(...)`, o `Meter` customizado do servico e os singletons/hosted services necessarios ao backlog, sem alterar o tracing validado em M2.

**Where**: `src/OrderService/Extensions/OtelExtensions.cs`, `src/OrderService/Program.cs`

**Depends on**: T1, T2

**Done when**:

- [ ] `AddOtelInstrumentation()` continua sendo a fronteira unica de configuracao OTel do servico
- [ ] `WithMetrics(...)` reutiliza o mesmo `OTEL_EXPORTER_OTLP_ENDPOINT` e protocolo OTLP gRPC ja usados por tracing
- [ ] Apenas o `Meter` customizado do `OrderService` e registrado nesta feature
- [ ] `OrderBacklogSnapshot`, `IOrderMetrics`/`OrderMetrics` e `OrderBacklogSampler` estao registrados em DI com ciclo de vida coerente com o design
- [ ] Nao foi aberto endpoint Prometheus proprio nem adicionada instrumentacao fora do escopo da feature

**Verification**:

- Local: build de `src/OrderService/OrderService.csproj` passa via SDK 10 em container
- Backend: nao aplicavel nesta tarefa

---

### T4: Integrar emissao de metricas ao fluxo real do `POST /orders`

**What**: Conectar o recorder do `OrderService` ao handler existente para gravar exatamente um counter e um histogram por request, preservando a semantica funcional de M2 e sem alterar contratos HTTP, Kafka ou spans.

**Where**: `src/OrderService/Program.cs`

**Depends on**: T3

**Done when**:

- [ ] O handler inicia medicao no comeco do `POST /orders`
- [ ] Existe uma unica variavel de `result` consolidada por request
- [ ] Cada caminho de saida registra apenas um dos resultados permitidos
- [ ] `orders.created.total` incrementa somente uma vez por request concluida
- [ ] `orders.create.duration` registra a duracao total imediatamente antes do retorno, com o mesmo `result` do counter
- [ ] O fluxo feliz e os caminhos `validation_failed`, `persist_failed`, `publish_failed` e `status_update_failed` preservam o comportamento funcional ja validado

**Verification**:

- Local: build de `src/OrderService/OrderService.csproj` passa e o endpoint continua retornando os mesmos status HTTP da baseline
- Backend: ainda nao obrigatorio, mas a tarefa deve deixar o servico pronto para T14

---

### T5: Criar o recorder de metricas do ProcessingWorker

**What**: Introduzir o recorder local do `ProcessingWorker` para centralizar `orders.processed.total` e `orders.processing.duration`, com a matriz fechada de resultados agregados do processamento por mensagem.

**Where**: `src/ProcessingWorker/Metrics/`

**Depends on**: feature `processing-worker-consumer-http-call` concluida

**Done when**:

- [ ] Existe `ProcessingMetrics` com `Meter` proprio do worker
- [ ] Os nomes canonicos `orders.processed.total` e `orders.processing.duration` estao definidos em um unico ponto
- [ ] Os resultados permitidos sao apenas `processed`, `invalid_payload`, `not_found`, `http_error`, `timeout`, `network_error`, `publish_failed` e `unexpected_error`
- [ ] O recorder encapsula labels de baixa cardinalidade sem depender de `traceId`, `orderId` ou payload
- [ ] Nao existe logica de gravacao de metricas movida para `OrderServiceClient`

**Verification**:

- Local: build de `src/ProcessingWorker/ProcessingWorker.csproj` passa via SDK 10 em container
- Backend: nao aplicavel nesta tarefa

---

### T6: Criar snapshot e refresher de lag do ProcessingWorker

**What**: Introduzir o snapshot em memoria e o refresh bounded do gauge `kafka.consumer.lag` do `ProcessingWorker`, mantendo qualquer leitura de offsets fora do callback observavel.

**Where**: `src/ProcessingWorker/Metrics/`, componentes de messaging associados

**Depends on**: T5

**Done when**:

- [ ] Existe `KafkaLagSnapshot` singleton com `topic`, `consumer_group`, `lag` e `lastUpdatedUtc`
- [ ] Existe `ProcessingLagRefresher` ou mecanismo equivalente com timeout curto e comportamento bounded
- [ ] O lag publicado e agregado por `topic=orders` e `consumer_group`, sem serie por particao nesta iteracao
- [ ] Ausencia temporaria de particoes atribuidas ou falha do broker nao derruba o host
- [ ] O callback futuro do gauge le apenas o ultimo snapshot conhecido ou fallback consistente

**Verification**:

- Local: build de `src/ProcessingWorker/ProcessingWorker.csproj` passa via SDK 10 em container
- Backend: nao aplicavel nesta tarefa

---

### T7: Ligar o bootstrap de metricas do ProcessingWorker ao pipeline OTel existente

**What**: Estender o bootstrap OpenTelemetry do `ProcessingWorker` para registrar `WithMetrics(...)`, o `Meter` customizado do worker e os componentes de snapshot/refresh de lag, sem alterar spans, logs ou consumo Kafka ja estabilizados.

**Where**: `src/ProcessingWorker/Extensions/OtelExtensions.cs`, `src/ProcessingWorker/Program.cs`

**Depends on**: T5, T6

**Done when**:

- [ ] `AddOtelInstrumentation()` do worker passa a registrar tracing e metrics sem perder a configuracao atual de tracing
- [ ] O `Meter` customizado do `ProcessingWorker` esta registrado explicitamente
- [ ] `KafkaLagSnapshot`, `IProcessingMetrics`/`ProcessingMetrics` e `ProcessingLagRefresher` estao ligados via DI
- [ ] O exporter de metricas reutiliza `OTEL_EXPORTER_OTLP_ENDPOINT=http://otelcol:4317` com OTLP gRPC
- [ ] Nao houve alteracao de contratos Kafka, payloads nem nomes de spans do worker

**Verification**:

- Local: build de `src/ProcessingWorker/ProcessingWorker.csproj` passa via SDK 10 em container
- Backend: nao aplicavel nesta tarefa

---

### T8: Integrar emissao de metricas ao pipeline do ProcessingWorker

**What**: Conectar o recorder do `ProcessingWorker` ao fluxo real de `ProcessMessageAsync(...)` e ao catch externo por mensagem, garantindo uma unica gravacao de counter e histogram por desfecho agregado.

**Where**: `src/ProcessingWorker/Worker.cs`

**Depends on**: T7

**Done when**:

- [ ] O processamento inicia medicao no comeco de `ProcessMessageAsync(...)`
- [ ] Existe uma unica variavel de `result` consolidada por mensagem
- [ ] `orders.processed.total` registra exatamente um resultado por mensagem tratada
- [ ] `orders.processing.duration` mede o processamento completo, incluindo desserializacao, chamada HTTP e publish quando aplicavel
- [ ] O catch externo registra `unexpected_error` uma unica vez, sem dupla contagem com caminhos internos
- [ ] Os desfechos `invalid_payload`, `not_found`, `http_error`, `timeout`, `network_error`, `publish_failed`, `processed` e `unexpected_error` permanecem coerentes com a classificacao funcional ja validada

**Verification**:

- Local: build de `src/ProcessingWorker/ProcessingWorker.csproj` passa e o worker continua consumindo/publicando conforme a baseline de M2
- Backend: ainda nao obrigatorio, mas a tarefa deve deixar o worker pronto para T14

---

### T9: Criar o recorder de metricas do NotificationWorker

**What**: Introduzir o recorder local do `NotificationWorker` para centralizar `notifications.persisted.total` e `notifications.persistence.duration`, mantendo o catalogo fechado de resultados desta etapa.

**Where**: `src/NotificationWorker/Metrics/`

**Depends on**: feature `notification-worker-consumer-persistencia` concluida

**Done when**:

- [ ] Existe `NotificationMetrics` com `Meter` proprio do worker
- [ ] Os nomes canonicos `notifications.persisted.total` e `notifications.persistence.duration` estao definidos em um unico ponto
- [ ] Os resultados permitidos do counter sao apenas `persisted`, `invalid_payload`, `persistence_failed`, `consume_failed` e `unexpected_error`
- [ ] Os resultados permitidos do histogram sao apenas `persisted` e `persistence_failed`
- [ ] O recorder encapsula labels de baixa cardinalidade e nao usa IDs ou mensagens de erro como dimensao

**Verification**:

- Local: build de `src/NotificationWorker/NotificationWorker.csproj` passa via SDK 10 em container
- Backend: nao aplicavel nesta tarefa

---

### T10: Criar snapshot e refresher de lag do NotificationWorker

**What**: Introduzir o snapshot em memoria e o refresh bounded do gauge `kafka.consumer.lag` do `NotificationWorker`, mantendo a leitura de lag fora do callback observavel.

**Where**: `src/NotificationWorker/Metrics/`, componentes de messaging associados

**Depends on**: T9

**Done when**:

- [ ] Existe `KafkaLagSnapshot` singleton com `topic=notifications`, `consumer_group`, `lag` e `lastUpdatedUtc`
- [ ] Existe `NotificationLagRefresher` ou mecanismo equivalente com timeout curto e custo bounded
- [ ] O lag e publicado apenas de forma agregada por `topic` e `consumer_group`
- [ ] Falhas temporarias ao refresh nao derrubam o host e nao alteram o fluxo de persistencia
- [ ] O callback futuro do gauge depende apenas do snapshot em memoria

**Verification**:

- Local: build de `src/NotificationWorker/NotificationWorker.csproj` passa via SDK 10 em container
- Backend: nao aplicavel nesta tarefa

---

### T11: Ligar o bootstrap de metricas do NotificationWorker ao pipeline OTel existente

**What**: Estender o bootstrap OpenTelemetry do `NotificationWorker` para registrar `WithMetrics(...)`, o `Meter` customizado do worker e os componentes de snapshot/refresh de lag, sem abrir escopo de novas instrumentacoes nem alterar a baseline de M2.

**Where**: `src/NotificationWorker/Extensions/OtelExtensions.cs`, `src/NotificationWorker/Program.cs`

**Depends on**: T9, T10

**Done when**:

- [ ] `AddOtelInstrumentation()` continua sendo a fronteira unica de observabilidade do worker
- [ ] O `Meter` customizado do `NotificationWorker` esta registrado explicitamente
- [ ] `KafkaLagSnapshot`, `INotificationMetrics`/`NotificationMetrics` e `NotificationLagRefresher` estao ligados via DI
- [ ] O exporter de metricas reutiliza o endpoint OTLP existente com protocolo gRPC
- [ ] Nao houve mudanca em contratos de evento, persistencia, logs ou spans alem do necessario para metricas

**Verification**:

- Local: build de `src/NotificationWorker/NotificationWorker.csproj` passa via SDK 10 em container
- Backend: nao aplicavel nesta tarefa

---

### T12: Integrar emissao de metricas ao pipeline do NotificationWorker

**What**: Conectar o recorder do `NotificationWorker` ao fluxo real de consumo e persistencia, separando explicitamente o counter por resultado da duracao do trecho de banco e evitando dupla contagem em `consume_failed` e `unexpected_error`.

**Where**: `src/NotificationWorker/Worker.cs`

**Depends on**: T11

**Done when**:

- [ ] O worker consolida um unico `result` por evento observavel
- [ ] `notifications.persisted.total` registra exatamente um resultado agregado por mensagem ou erro tecnico observavel
- [ ] `notifications.persistence.duration` mede apenas `Add(...)` + `SaveChangesAsync(...)`
- [ ] Payload invalido nao gera medicao de persistencia bem-sucedida
- [ ] Falha de banco registra `persistence_failed` tanto no histogram quanto no counter final, sem contaminar o caminho feliz
- [ ] `consume_failed` vindo de `ConsumeException` e do error handler Kafka e contabilizado sem duplicidade por evento observavel

**Verification**:

- Local: build de `src/NotificationWorker/NotificationWorker.csproj` passa e o worker continua persistindo e classificando falhas como na baseline de M2
- Backend: ainda nao obrigatorio, mas a tarefa deve deixar o worker pronto para T14

---

### T13: Revalidar build e fluxo funcional sem regressao da baseline M2

**What**: Executar a validacao integrada minima para garantir que a instrumentacao de metricas nao alterou os fluxos funcionais, contratos ou spans ja estabilizados em M2.

**Where**: build da solution, Docker Compose, endpoints, Kafka, PostgreSQL e Tempo ja usados na baseline

**Depends on**: T4, T8, T12

**Done when**:

- [ ] `dotnet build otel-poc.sln` passa via SDK 10 em container Docker
- [ ] `docker compose up -d --build` conclui sem novo erro funcional relacionado a metricas
- [ ] Um `POST /orders` feliz continua gerando mensagem em `orders`, publish em `notifications` e persistencia em `notification_results`
- [ ] Um pedido invalido continua retornando erro de validacao sem tocar o fluxo feliz
- [ ] Um cenario com Kafka indisponivel continua expondo `publish_failed` no `OrderService` sem derrubar os workers
- [ ] Um payload invalido em `notifications` continua nao persistindo no banco e nao derruba o `NotificationWorker`
- [ ] O trace distribuido principal continua observavel no Tempo com os mesmos spans basicos da baseline

**Verification**:

- Local: validar por build em container, `docker compose`, chamadas HTTP, consumo Kafka e consultas PostgreSQL
- Tempo: confirmar ausencia de drift no caminho `POST /orders` -> `kafka publish orders` -> `kafka consume orders` -> `GET /orders/{id}` -> `kafka publish notifications` -> `kafka consume notifications` -> span DB

---

### T14: Validar as series no backend LGTM com nomes normalizados e baixa cardinalidade

**What**: Confirmar no backend de metricas que as series customizadas chegam pelo pipeline OTLP existente, aparecem com `service.name` coerente e respeitam a politica de labels baixas em cardinalidade, incluindo a validacao explicita dos nomes normalizados em Explore/Prometheus.

**Where**: Grafana Explore / Prometheus no LGTM e, se necessario, logs do `otelcol`

**Depends on**: T13

**Done when**:

- [ ] As series do `OrderService` sao consultaveis no backend para `orders.created.total`, `orders.create.duration` e `orders.backlog.current`
- [ ] As series do `ProcessingWorker` sao consultaveis no backend para `orders.processed.total`, `orders.processing.duration` e `kafka.consumer.lag`
- [ ] As series do `NotificationWorker` sao consultaveis no backend para `notifications.persisted.total`, `notifications.persistence.duration` e `kafka.consumer.lag`
- [ ] A validacao aceita explicitamente a forma normalizada do backend, como `orders_created_total`, `orders_create_duration`, `orders_backlog_current`, `orders_processed_total`, `orders_processing_duration`, `notifications_persisted_total`, `notifications_persistence_duration` e `kafka_consumer_lag`
- [ ] Para histograms, existe ao menos evidencia de serie compatibilizada no backend, como base normalizada ou series derivadas `_bucket`, `_sum` ou `_count`
- [ ] Nenhuma query de labels revela `orderId`, `traceId`, `spanId`, `description`, payload bruto ou outro identificador de alta cardinalidade
- [ ] Se necessario para depuracao, os logs do `otelcol` permitem confirmar que os datapoints sairam dos servicos sem exigir dashboard provisionado

**Verification**:

- Local: gerar trafego real para caminho feliz e para pelo menos um erro em cada servico antes das consultas
- Backend: usar Explore/Prometheus para consultar as series por `service.name` e revisar labels expostas por cada metrica

---

## Validation Notes

- O host local continua sem .NET 10 SDK; o build deve seguir validado via container Docker com `mcr.microsoft.com/dotnet/sdk:10.0`
- A implementacao deve preservar spans, logs, contratos Kafka, payloads, persistencia e nomes de fluxos estabilizados em M2
- Gauges desta feature devem usar somente snapshots em memoria atualizados fora do callback do `ObservableGauge`
- A ausencia de dashboard ou alerta nao bloqueia a conclusao desta feature; a fonte de verdade de validacao e o backend LGTM/Prometheus
- Qualquer desvio de cardinalidade, nome de serie ou comportamento funcional em relacao a `STATE.md` deve ser tratado como regressao ate prova em contrario

---

## Parallel Execution Map

```text
OrderService:
  T1 -> T2 -> T3 -> T4

ProcessingWorker:
  T5 -> T6 -> T7 -> T8

NotificationWorker:
  T9 -> T10 -> T11 -> T12

Integracao:
  T4 + T8 + T12 -> T13 -> T14
```