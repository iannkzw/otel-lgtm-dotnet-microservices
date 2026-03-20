# Metricas Customizadas - Specification

**Milestone**: M3 - Metricas e Observabilidade Avancada
**Status**: Specified

---

## Problem Statement

O milestone M2 ja fechou o fluxo distribuido ponta a ponta entre `OrderService`,
`ProcessingWorker` e `NotificationWorker`, com traces e logs observaveis no
stack LGTM existente. Porem, a PoC ainda nao emite metricas customizadas de
aplicacao: hoje o codigo so configura tracing, nao registra `Meter`, nao cria
`Counter`, `Histogram` ou `ObservableGauge`, e portanto nao ha series de
negocio/operacao que permitam acompanhar backlog, throughput e latencia dos
tres servicos no backend de metricas.

Esta feature precisa adicionar um catalogo minimo de metricas customizadas, com
nomes, tags e pontos de emissao coerentes com a baseline validada de M2, sem
reabrir spans, logs, payloads Kafka, contratos HTTP ou persistencia ja
estabilizados. A exportacao deve reutilizar o caminho OTLP ja existente
(`service -> otelcol -> LGTM`), e a validacao deve provar que as series chegam
ao backend antes de qualquer trabalho de dashboard ou alerta.

## Goals

- [ ] Adicionar bootstrap de metricas OpenTelemetry nos tres servicos sem
      alterar a semantica do fluxo distribuido de M2
- [ ] Definir um catalogo minimo de counters, histograms e gauges para
      `OrderService`, `ProcessingWorker` e `NotificationWorker`
- [ ] Limitar tags customizadas a dimensoes de baixa cardinalidade e estaveis
- [ ] Reutilizar `OTEL_EXPORTER_OTLP_ENDPOINT` e a pipeline `metrics` ja
      existente no collector/LGTM
- [ ] Definir criterios objetivos para validar a chegada das metricas no
      backend sem depender ainda de dashboards ou alertas
- [ ] Tornar backlog e lag observaveis sem consultas pesadas nem callbacks que
      possam degradar o fluxo principal

## Out of Scope

- Dashboards Grafana, provisionamento JSON ou queries finais de painel
- Alertas, contact points ou regras de avaliacao no Grafana
- Retry, DLQ, outbox, idempotencia adicional ou qualquer mudanca de semantica do
  fluxo de M2
- Alteracoes em payloads Kafka, topicos, spans manuais, contratos HTTP ou tabelas
  ja estabilizadas
- Exemplars, trace-to-metrics linking ou correlacao automatica por exemplar
- Exposicao de endpoint Prometheus proprio em cada servico
- Metricas por `orderId`, `traceId`, payload ou qualquer outra dimensao de alta
  cardinalidade

---

## Current Baseline

### Export path ja existente

- `docker-compose.yaml` ja injeta `OTEL_EXPORTER_OTLP_ENDPOINT=http://otelcol:4317`
  nos tres servicos
- `otelcol.yaml` ja possui pipeline `metrics` com receiver OTLP e exporter
  `otlphttp/metrics` apontando para `lgtm:4318/v1/metrics`
- O stack ja inclui `telemetrygen-metrics`, o que confirma que o collector e o
  LGTM aceitam metricas OTLP no ambiente atual

### Lacuna atual no codigo

- `OrderService.Extensions.OtelExtensions` registra apenas tracing
- `ProcessingWorker.Extensions.OtelExtensions` registra apenas tracing
- `NotificationWorker.Extensions.OtelExtensions` registra apenas tracing
- Nao existe nenhum `Meter` customizado sob `src/`

### Baseline funcional a preservar

- `POST /orders` persiste, publica no Kafka e atualiza status no banco
- `ProcessingWorker` consome `orders`, chama `GET /orders/{id}` e publica em
  `notifications`
- `NotificationWorker` consome `notifications` e persiste em `notification_results`
- Os caminhos de erro ja classificados em M2 continuam sendo a fonte da verdade
  para os resultados das metricas

---

## Metric Design Principles

### Naming

- Os nomes das metricas devem seguir o catalogo abaixo exatamente, preservando a
  intencao do roadmap e aderindo ao comportamento real da PoC
- A separacao por servico deve acontecer prioritariamente por `service.name`
  vindo do resource OpenTelemetry, nao por duplicacao de nomes com prefixos
  artificiais por ambiente

### Allowed dimensions

As metricas customizadas desta feature podem usar apenas estas tags:

- `result`
- `status`
- `topic`
- `consumer_group`

Qualquer outra tag precisa ser tratada como fora de escopo nesta iteracao.

### Forbidden dimensions

Estas tags NAO podem ser emitidas por metricas customizadas:

- `orderId`
- `traceId`
- `spanId`
- `description`
- payload bruto
- mensagem de excecao
- URL completa com IDs dinamicos
- qualquer identificador unico por evento

### Meter registration

- Cada servico deve registrar explicitamente seu `Meter` customizado no bootstrap
  de OpenTelemetry
- O bootstrap deve adicionar `WithMetrics(...)` sem remover o tracing ja valido
- A exportacao OTLP deve continuar apontando para o mesmo endpoint ja usado por
  traces e logs

---

## Metric Catalog

## OrderService

### Counter: `orders.created.total`

**Type**: Counter
**Unit**: `{order}`
**Dimensions**:

- `result`: `created`, `validation_failed`, `persist_failed`, `publish_failed`, `status_update_failed`

**Emission points**:

1. Incrementar `result=validation_failed` antes do retorno de `ValidationProblem`
2. Incrementar `result=persist_failed` no catch da primeira persistencia
3. Incrementar `result=publish_failed` no catch do publish Kafka
4. Incrementar `result=status_update_failed` no catch do update final de status
5. Incrementar `result=created` apenas quando a resposta `201 Created` estiver pronta

**Rationale**:

- A metrica representa o desfecho do fluxo de criacao, nao apenas pedidos bem-sucedidos
- O eixo `result` reaproveita a taxonomia ja observavel em logs e spans

### Histogram: `orders.create.duration`

**Type**: Histogram
**Unit**: `ms`
**Dimensions**:

- `result`: mesmo conjunto de `orders.created.total`

**Emission points**:

1. Iniciar medicao no comeco do handler `POST /orders`
2. Registrar a duracao total imediatamente antes de cada retorno do endpoint

**Scope of measurement**:

- Inclui validacao, persistencia inicial, publish Kafka, update final de status e
  criacao da resposta HTTP

### Gauge: `orders.backlog.current`

**Type**: ObservableGauge
**Unit**: `{order}`
**Dimensions**:

- `status`: `pending_publish`, `publish_failed`

**Emission points**:

1. Callback observavel agregando quantidade atual de pedidos com status
   `pending_publish`
2. Callback observavel agregando quantidade atual de pedidos com status
   `publish_failed`

**Constraints**:

- O gauge deve emitir apenas esses dois estados
- A consulta deve ser agregada e bounded; nao pode iterar por pedido
- Falha na leitura do gauge nao pode quebrar requests HTTP nem mudar o fluxo de M2

---

## ProcessingWorker

### Counter: `orders.processed.total`

**Type**: Counter
**Unit**: `{message}`
**Dimensions**:

- `result`: `processed`, `invalid_payload`, `not_found`, `http_error`, `timeout`, `network_error`, `publish_failed`, `unexpected_error`

**Emission points**:

1. Incrementar `result=invalid_payload` em JSON invalido ou `orderId` ausente/invalido
2. Incrementar `result=not_found`, `http_error`, `timeout` ou `network_error`
   conforme o resultado retornado por `OrderServiceClient`
3. Incrementar `result=publish_failed` quando o publish em `notifications` falhar
4. Incrementar `result=unexpected_error` no catch externo por mensagem
5. Incrementar `result=processed` apenas quando o evento enriquecido for publicado com sucesso

### Histogram: `orders.processing.duration`

**Type**: Histogram
**Unit**: `ms`
**Dimensions**:

- `result`: mesmo conjunto de `orders.processed.total`

**Emission points**:

1. Iniciar medicao quando `ProcessMessageAsync(...)` assumir a mensagem consumida
2. Registrar a duracao final em todos os caminhos de saida, incluindo erros de
   enriquecimento e falha de publish

**Scope of measurement**:

- Inclui extracao do contexto, desserializacao, chamada HTTP ao `OrderService`
  e publish em `notifications` quando aplicavel

### Gauge: `kafka.consumer.lag`

**Type**: ObservableGauge
**Unit**: `{message}`
**Dimensions**:

- `topic`: `orders`
- `consumer_group`: valor de `KAFKA_GROUP_ID_PROCESSING`

**Emission points**:

1. Atualizar um estado agregado de lag apos consumes bem-sucedidos e/ou eventos de
   atribuicao de particao
2. O callback do gauge deve publicar apenas o valor agregado mais recente

**Constraints**:

- A feature deve preferir lag agregado do grupo para o topic, nao series por particao
- O callback do gauge nao deve fazer chamadas bloqueantes ao broker em cada coleta

---

## NotificationWorker

### Counter: `notifications.persisted.total`

**Type**: Counter
**Unit**: `{message}`
**Dimensions**:

- `result`: `persisted`, `invalid_payload`, `persistence_failed`, `consume_failed`, `unexpected_error`

**Emission points**:

1. Incrementar `result=consume_failed` nos caminhos em que `ConsumeException` ou
   o error handler classifiquem falha tecnica de consumo
2. Incrementar `result=invalid_payload` em JSON invalido ou falha de validacao do evento
3. Incrementar `result=persistence_failed` quando `SaveChangesAsync(...)` falhar
4. Incrementar `result=unexpected_error` no catch externo por mensagem
5. Incrementar `result=persisted` apenas apos persistencia bem-sucedida em
   `notification_results`

### Histogram: `notifications.persistence.duration`

**Type**: Histogram
**Unit**: `ms`
**Dimensions**:

- `result`: `persisted`, `persistence_failed`

**Emission points**:

1. Iniciar medicao imediatamente antes de adicionar/salvar a entidade no `DbContext`
2. Registrar a duracao ao final da tentativa de persistencia, com `result`
   coerente com o desfecho do banco

**Scope of measurement**:

- Mede apenas o trecho de persistencia em banco, nao o processamento completo da
  mensagem

### Gauge: `kafka.consumer.lag`

**Type**: ObservableGauge
**Unit**: `{message}`
**Dimensions**:

- `topic`: `notifications`
- `consumer_group`: valor de `KAFKA_GROUP_ID_NOTIFICATION`

**Emission points**:

1. Atualizar estado agregado de lag conforme o worker consumir mensagens ou
   receber atribuicoes de particao
2. Expor no callback apenas o ultimo valor agregado disponivel

**Constraints**:

- Mesmo nome de metrica do `ProcessingWorker`, separado por `service.name`
- Nao emitir series por particao nesta iteracao

---

## Export Strategy

### Service side

1. Cada servico deve estender seu `AddOtelInstrumentation()` para incluir
   `WithMetrics(...)`
2. O bootstrap deve registrar explicitamente o `Meter` customizado do servico
3. O exporter de metricas deve reutilizar `OTEL_EXPORTER_OTLP_ENDPOINT`
   apontando para `http://otelcol:4317`
4. O protocolo deve permanecer OTLP gRPC, alinhado ao bootstrap ja usado por
   tracing

### Collector side

1. O collector ja recebe metricas por `receivers.otlp`
2. A pipeline `metrics` ja exporta para `lgtm:4318/v1/metrics`
3. Esta feature nao deve alterar processors, exporters ou receivers do collector
   salvo se algum bloqueio concreto for encontrado na implementacao

### Backend side

1. O LGTM ja centraliza Prometheus/Grafana e deve continuar sendo a fonte de
   verdade para consulta das metricas
2. A validacao inicial desta feature deve acontecer em Explore/Prometheus e, se
   necessario, no `debug` exporter do collector, sem depender de dashboard pronto

---

## User Stories

### P1: Bootstrap de metricas nos tres servicos sem regressao do fluxo validado ⭐ MVP

**User Story**: Como mantenedor da PoC, quero adicionar metricas customizadas
nos tres servicos reaproveitando o pipeline OTLP atual, para observar throughput,
latencia e backlog sem reabrir o fluxo distribuido validado em M2.

**Why P1**: Sem bootstrap de metrics, M3 nao consegue avancar para dashboard e
alertas com dados reais de aplicacao.

**Acceptance Criteria**:

1. WHEN a feature for implementada THEN `OrderService`, `ProcessingWorker` e
   `NotificationWorker` SHALL registrar `WithMetrics(...)` alem do tracing ja existente
2. WHEN o servico iniciar THEN ele SHALL continuar exportando para
   `OTEL_EXPORTER_OTLP_ENDPOINT` ja configurado no compose
3. WHEN requests e mensagens reais forem processadas THEN as series customizadas
   SHALL aparecer no backend de metricas sem quebrar traces, logs ou contratos
4. WHEN o collector e o LGTM estiverem saudaveis THEN nenhum endpoint extra de
   metricas SHALL ser necessario nos servicos
5. WHEN houver falha no backend de metricas THEN o fluxo funcional de M2 SHALL
   continuar operando conforme a baseline atual

**Independent Test**: Subir o compose, gerar um `POST /orders` com consumo completo
ate o `NotificationWorker` e confirmar que pelo menos uma serie de cada servico
aparece no backend via Explore/Prometheus.

---

### P1: Expor metricas de OrderService coerentes com os desfechos reais do endpoint ⭐ MVP

**User Story**: Como operador da PoC, quero ver quantidade, duracao e backlog do
`OrderService`, para identificar criacao bem-sucedida, falhas de publish e pedidos
presos antes do Kafka.

**Acceptance Criteria**:

1. WHEN `POST /orders` terminar com sucesso THEN `orders.created.total` SHALL
   incrementar com `result=created`
2. WHEN o endpoint falhar por validacao, persistencia, publish ou update final
   THEN `orders.created.total` SHALL registrar o `result` correspondente
3. WHEN o endpoint retornar em qualquer caminho THEN `orders.create.duration`
   SHALL registrar a duracao total com o mesmo `result`
4. WHEN o collector solicitar observacoes do gauge THEN `orders.backlog.current`
   SHALL publicar apenas os estados `pending_publish` e `publish_failed`
5. WHEN houver pedidos acumulados nesses estados THEN o backend SHALL refletir
   backlog sem expor IDs de pedido como label

**Independent Test**: Executar um pedido feliz, um pedido invalido e um pedido
com Kafka indisponivel; validar os tres resultados em `orders.created.total`, a
presenca de amostras em `orders.create.duration` e um valor coerente em
`orders.backlog.current` apos falha de publish.

---

### P1: Expor metricas de ProcessingWorker para processamento e atraso de consumo ⭐ MVP

**User Story**: Como operador da PoC, quero ver resultado, duracao e lag do
`ProcessingWorker`, para identificar se o worker esta consumindo, enriquecendo e
publicando dentro do esperado.

**Acceptance Criteria**:

1. WHEN uma mensagem valida for consumida e publicada em `notifications` THEN
   `orders.processed.total` SHALL incrementar com `result=processed`
2. WHEN ocorrer payload invalido, `404`, erro HTTP, timeout, erro de rede,
   falha de publish ou erro inesperado THEN o counter SHALL registrar exatamente
   um resultado agregado coerente com o desfecho observado
3. WHEN qualquer mensagem terminar de ser tratada THEN `orders.processing.duration`
   SHALL registrar a duracao total do processamento com o `result` correspondente
4. WHEN houver diferenca entre offset atual e offset mais recente do topic
   `orders` THEN `kafka.consumer.lag` SHALL refletir esse atraso agregado por
   `topic` e `consumer_group`
5. WHEN a mensagem nao puder ser correlacionada por trace W3C THEN o comportamento
   das metricas SHALL continuar coerente com o resultado funcional, sem depender
   de `traceId` como label

**Independent Test**: Executar o caminho feliz e pelo menos um cenario de erro de
enriquecimento; validar o counter por `result`, a duracao em histogram e o gauge
de lag com o worker parado temporariamente ou sob backlog controlado.

---

### P1: Expor metricas de NotificationWorker para persistencia e atraso de consumo ⭐ MVP

**User Story**: Como operador da PoC, quero ver resultado de persistencia,
duracao de escrita e lag do `NotificationWorker`, para saber se o ultimo hop do
pipeline esta concluindo ou travando no banco/Kafka.

**Acceptance Criteria**:

1. WHEN uma notificacao valida for persistida THEN `notifications.persisted.total`
   SHALL incrementar com `result=persisted`
2. WHEN houver `consume_failed`, `invalid_payload`, `persistence_failed` ou erro
   inesperado THEN o counter SHALL registrar o `result` agregado correspondente
3. WHEN uma tentativa de persistencia em banco ocorrer THEN
   `notifications.persistence.duration` SHALL registrar a duracao com `result`
   `persisted` ou `persistence_failed`
4. WHEN houver atraso no consumo do topic `notifications` THEN `kafka.consumer.lag`
   SHALL refletir esse valor agregado por `topic` e `consumer_group`
5. WHEN o payload for invalido antes do banco THEN a feature SHALL nao fabricar
   medida de persistencia bem-sucedida nem labels de alta cardinalidade

**Independent Test**: Executar o caminho feliz, publicar um payload invalido e
simular falha de banco; validar `notifications.persisted.total` por resultado,
`notifications.persistence.duration` para sucesso/falha e o gauge de lag do worker.

---

### P1: Validar chegada das metricas ao backend sem depender de dashboard ⭐ MVP

**User Story**: Como responsavel pela baseline de observabilidade, quero provar
que as metricas chegam corretamente ao backend antes de desenhar dashboards e
alertas, para separar implementacao da coleta de implementacao de visualizacao.

**Acceptance Criteria**:

1. WHEN a feature for implementada e o compose estiver de pe THEN o collector
   SHALL receber metricas OTLP dos tres servicos
2. WHEN series customizadas forem emitidas THEN elas SHALL ser consultaveis em
   Explore/Prometheus no LGTM
3. WHEN for necessario depurar a entrega THEN os logs do `otelcol` com exporter
   `debug` SHALL permitir confirmar que os datapoints sairam dos servicos
4. WHEN a validacao desta feature terminar THEN nao SHALL ser necessario ter
   dashboard provisionado nem regra de alerta criada
5. WHEN a query de validacao listar labels das series THEN nao SHALL existir
   `orderId`, `traceId` ou outro valor de alta cardinalidade emitido pela feature

**Independent Test**: Usar Explore/Prometheus para consultar diretamente as
metricas nomeadas nesta spec apos gerar trafego real; complementar, se preciso,
com inspecao dos logs do `otelcol` para confirmar exportacao.

## Edge Cases

- WHEN o `OrderService` retornar `ValidationProblem` antes de tocar banco ou Kafka
  THEN `orders.created.total` e `orders.create.duration` ainda SHALL registrar
  `result=validation_failed`
- WHEN o callback de `orders.backlog.current` falhar ao consultar o banco THEN a
  falha SHALL ser absorvida com log diagnostico e sem derrubar o processo
- WHEN os workers estiverem sem particoes atribuidas temporariamente THEN
  `kafka.consumer.lag` SHALL poder emitir `0` ou ultimo valor conhecido, desde
  que o comportamento seja consistente e documentado na implementacao
- WHEN `ProcessingWorker` falhar antes do publish por `unexpected_error` THEN o
  counter e o histogram SHALL fechar o resultado como `unexpected_error`
- WHEN `NotificationWorker` falhar no banco apos payload valido THEN
  `notifications.persistence.duration` SHALL registrar `result=persistence_failed`
  e `notifications.persisted.total` SHALL nao contar como `persisted`
- WHEN o collector estiver indisponivel THEN a feature SHALL nao alterar o
  comportamento funcional de requests, consumes, publishes ou persistencia

## Validation Criteria

### Backend checks

1. Subir o ambiente com `docker compose up -d --build`
2. Confirmar saude de `otelcol`, `lgtm`, `order-service`, `processing-worker` e
   `notification-worker`
3. Gerar pelo menos um fluxo feliz com `POST /orders`
4. Consultar no Grafana Explore / Prometheus as seguintes series:
   - `orders.created.total`
   - `orders.create.duration`
   - `orders.backlog.current`
   - `orders.processed.total`
   - `orders.processing.duration`
   - `notifications.persisted.total`
   - `notifications.persistence.duration`
   - `kafka.consumer.lag`
5. Confirmar que cada serie aparece com `service.name` coerente e labels baixas
   em cardinalidade

### Happy path

1. Criar um pedido valido em `POST /orders`
2. Aguardar consumo pelo `ProcessingWorker` e persistencia pelo `NotificationWorker`
3. Confirmar incremento de:
   - `orders.created.total{result="created"}`
   - `orders.processed.total{result="processed"}`
   - `notifications.persisted.total{result="persisted"}`
4. Confirmar amostras recentes em:
   - `orders.create.duration`
   - `orders.processing.duration`
   - `notifications.persistence.duration`

### Error path: falha de publish no OrderService

1. Induzir indisponibilidade de Kafka para o `OrderService`
2. Executar `POST /orders`
3. Confirmar incremento de `orders.created.total{result="publish_failed"}`
4. Confirmar backlog em `orders.backlog.current{status="publish_failed"}`

### Error path: falha de enriquecimento no ProcessingWorker

1. Produzir ou reenfileirar mensagem que leve a `404`, timeout ou erro HTTP
2. Confirmar incremento em `orders.processed.total` com o `result` correspondente
3. Confirmar amostra em `orders.processing.duration` com o mesmo `result`

### Error path: payload invalido ou falha de persistencia no NotificationWorker

1. Publicar payload invalido em `notifications`
2. Confirmar incremento de `notifications.persisted.total{result="invalid_payload"}`
3. Tornar o PostgreSQL indisponivel para uma mensagem valida
4. Confirmar incremento de `notifications.persisted.total{result="persistence_failed"}`
5. Confirmar amostra em `notifications.persistence.duration{result="persistence_failed"}`

## Success Criteria

- [ ] Os tres servicos passam a registrar metricas customizadas via OTLP usando o
      collector/LGTM ja existentes
- [ ] O catalogo minimo de counters, histograms e gauges fica alinhado aos pontos
      reais de emissao do codigo e aos desfechos consolidados em M2
- [ ] Nenhuma metrica customizada introduz labels de alta cardinalidade ou
      dependencias que reabram spans, contratos ou persistencia da baseline
- [ ] As series ficam consultaveis no backend antes de qualquer trabalho de
      dashboard ou alerta
- [ ] Os sinais de backlog e lag passam a existir de forma agregada e segura para
      a PoC