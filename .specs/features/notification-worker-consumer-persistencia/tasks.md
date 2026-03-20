# NotificationWorker Consumer + Persistência — Tasks

**Design**: `.specs/features/notification-worker-consumer-persistencia/design.md`
**Status**: Completed

---

## Execution Plan

### Phase 1: Infra de consumo e persistência

```
T1 (dependencias/config) -> T2 (contratos + helper W3C) -> T3 (modelo e DbContext)
T2 -> T4 (consumer Kafka)
```

### Phase 2: Pipeline do worker e observabilidade

```
T3 + T4 -> T5 (pipeline consume -> validate -> persist)
T5 -> T6 (OTel + logs + classificacao de falhas)
```

### Phase 3: Validacao local e no Tempo

```
T6 -> T7 (smoke tests locais) -> T8 (validacao no Tempo)
```

---

## Task Breakdown

### T1: Adicionar dependencias e configuracoes do NotificationWorker

**What**: Atualizar o projeto para suportar consumer Kafka, EF Core PostgreSQL e
configuracoes necessarias ao fluxo.
**Where**: `src/NotificationWorker/NotificationWorker.csproj`,
`src/NotificationWorker/appsettings*.json`, `docker-compose.yaml`
**Depends on**: feature `processing-worker-consumer-http-call` concluida

**Done when**:
- [ ] `Confluent.Kafka`, `Npgsql.EntityFrameworkCore.PostgreSQL` e
- [x] `Confluent.Kafka`, `Npgsql.EntityFrameworkCore.PostgreSQL` e
      `OpenTelemetry.Instrumentation.EntityFrameworkCore` estao referenciados no
      `NotificationWorker`
- [x] Existe configuracao para `KAFKA_TOPIC_NOTIFICATIONS` e
      `KAFKA_GROUP_ID_NOTIFICATION`
- [x] Existe configuracao para `POSTGRES_CONNECTION_STRING`
- [x] O build do projeto passa no ambiente validado da solution

**Verification**:
- Local: build do projeto via SDK 10 em container passa sem novos erros
- Tempo: nao aplicavel nesta tarefa

---

### T2: Introduzir contrato consumido e helper W3C no NotificationWorker

**What**: Criar ou adaptar os contratos minimos de entrada e o helper de
tracing Kafka para extrair `traceparent` e `tracestate`.
**Where**: `src/NotificationWorker/Contracts/`,
`src/NotificationWorker/Messaging/`
**Depends on**: T1

**Done when**:
- [x] Existe contrato coerente com o payload minimo de `notifications`
- [x] O helper expoe `Extract(Headers?)`
- [x] Headers ausentes ou invalidos retornam ausencia de contexto sem excecao
- [x] Nao existe logica W3C inline no `Worker`

**Verification**:
- Local: build passa e o contrato do payload continua inalterado
- Tempo: nao aplicavel nesta tarefa

---

### T3: Implementar o modelo interno e a persistência minima no PostgreSQL

**What**: Criar entidade, `DbContext` e bootstrap minimo do schema da tabela
`notification_results`.
**Where**: `src/NotificationWorker/Data/`, `src/NotificationWorker/Program.cs`
**Depends on**: T1

**Done when**:
- [x] Existe entidade `PersistedNotification` com os seis campos do evento mais
      `persistedAtUtc` e `traceId`
- [x] Existe `NotificationDbContext` configurado para PostgreSQL
- [x] A tabela `notification_results` possui colunas coerentes com o design
- [x] O startup cria o schema minimo necessario em ambiente limpo sem DDL manual

**Verification**:
- Local: ambiente sobe e a tabela existe no PostgreSQL apos o startup
- Tempo: nao aplicavel nesta tarefa

---

### T4: Implementar o consumer Kafka do topic `notifications`

**What**: Registrar e configurar o consumer com group id dedicado, assinando o
topic `notifications` e entregando mensagens ao pipeline do worker.
**Where**: `src/NotificationWorker/Program.cs`, `src/NotificationWorker/Worker.cs`
**Depends on**: T2

**Done when**:
- [x] O worker assina o topic `notifications`
- [x] Falhas de `Consume` sao classificadas como `consume_failed`
- [x] O loop respeita `CancellationToken`
- [x] O host continua saudavel apos falhas de consumo

**Verification**:
- Local: ao induzir indisponibilidade do Kafka, o container permanece em `Up`
- Tempo: nao aplicavel diretamente; a validacao do trace fica para T8

---

### T5: Implementar o pipeline consume -> validate -> persist

**What**: Orquestrar no `Worker` a extracao de contexto, criacao do span de
consumo, desserializacao, validacao semantica e persistencia da mensagem valida.
**Where**: `src/NotificationWorker/Worker.cs`
**Depends on**: T3, T4

**Done when**:
- [x] O span manual `kafka consume notifications` e criado com
      `ActivityKind.Consumer`
- [x] O payload invalido e classificado como `invalid_payload`
- [x] O payload valido gera uma linha em `notification_results` com `traceId`
      vindo do contexto corrente
- [x] Falhas de persistencia sao classificadas como `persistence_failed`

**Verification**:
- Local: mensagem valida persiste no banco; mensagem invalida nao persiste
- Tempo: o span manual de consumo passa a existir no `notification-worker`

---

### T6: Expandir observabilidade do NotificationWorker

**What**: Ajustar bootstrap OTel, `ActivitySource` e logs para refletir com
clareza o fluxo Kafka consumer -> PostgreSQL e as tres classes de falha.
**Where**: `src/NotificationWorker/Extensions/OtelExtensions.cs`,
`src/NotificationWorker/Worker.cs`, componentes de persistencia associados
**Depends on**: T5

**Done when**:
- [x] O `ActivitySource` do worker inclui o span `kafka consume notifications`
- [x] A instrumentacao EF Core gera spans de banco no mesmo trace
- [x] Logs relevantes incluem `orderId`, `TraceId` e `SpanId`
- [x] `consume_failed`, `invalid_payload` e `persistence_failed` ficam
      distinguiveis em trace e log

**Verification**:
- Local: logs mostram classificacao consistente dos cenarios feliz e de erro
- Tempo: spans de consumo e banco aparecem com correlacao esperada

---

### T7: Executar smoke tests locais do fluxo feliz e dos caminhos de erro

**What**: Subir o ambiente, gerar pedidos reais e validar comportamento visivel
em Kafka, PostgreSQL e logs do worker.
**Where**: execucao local via Docker Compose e ferramentas ja usadas no projeto
**Depends on**: T6

**Done when**:
- [x] `docker compose up -d --build order-service processing-worker notification-worker kafka postgres otelcol lgtm` conclui sem novo erro funcional
- [x] Um `POST /orders` saudavel gera nova linha em `notification_results`
- [x] Uma mensagem invalida em `notifications` nao gera nova linha e nao derruba
      o worker
- [x] Uma falha de persistencia nao derruba o `notification-worker`

**Verification**:
- Local: logs, `psql` e topic `notifications` confirmam os comportamentos
- Tempo: nao e o foco principal desta tarefa

---

### T8: Validar o trace distribuido completo no Tempo

**What**: Confirmar no Tempo que o ultimo hop do `NotificationWorker` aparece no
mesmo `TraceId` do pedido criado no `OrderService` quando houver headers W3C
validos.
**Where**: Grafana Tempo e inspecao dos artefatos locais
**Depends on**: T7

**Done when**:
- [x] O caminho feliz mostra `POST /orders` -> `kafka publish orders` ->
      `kafka consume orders` -> `GET /orders/{id}` ->
      `kafka publish notifications` -> `kafka consume notifications` -> span DB
- [x] O `TraceId` persistido em `notification_results` coincide com o exibido no
      Tempo para o fluxo feliz
- [x] O caminho de `invalid_payload` mostra span de consumo com erro e ausencia
      de span DB bem-sucedido subsequente
- [x] O caminho de `persistence_failed` mostra erro observavel no hop de banco
      sem encerrar o host

**Verification**:
- Local: consulta ao banco confirma o `traceId` persistido do caminho feliz
- Tempo: busca pelo `TraceId` mostra os spans dos tres servicos e o hop final do
  `notification-worker`

---

## Validation Notes

- O build do `NotificationWorker` e da solution passaram via SDK 10 em container Docker.
- O fluxo feliz foi validado com `POST /orders`, logs do `notification-worker`, consulta direta a `notification_results` e comparacao do `traceId` persistido com o `TraceId` retornado pelo Tempo.
- O caminho `invalid_payload` foi validado com JSON malformado publicado manualmente em `notifications`, sem nova linha no PostgreSQL e com span `kafka consume notifications` em erro no Tempo.
- O caminho `persistence_failed` foi validado com PostgreSQL parado, mensagem valida em `notifications`, erro observavel de banco no Tempo e `notification-worker` permanecendo em `Up`.
- O caminho `consume_failed` foi validado com Kafka parado temporariamente e logs explicitos `Classification=consume_failed` emitidos pelo error handler do consumer, sem derrubar o host.
- O build deve continuar sendo validado via SDK 10 em container Docker enquanto
  o host local permanecer sem .NET 10.
- A validacao local cobriu explicitamente os tres erros definidos na spec: `consume_failed`, `invalid_payload` e `persistence_failed`.
- A validacao no Tempo confirmou que o `TraceId` do banco e o `TraceId` visualizado no trace feliz coincidem.

---

## Parallel Execution Map

```
Phase 1:
  T1 -> T2
  T1 -> T3
  T2 -> T4

Phase 2:
  T3 + T4 -> T5 -> T6

Phase 3:
  T6 -> T7 -> T8
```