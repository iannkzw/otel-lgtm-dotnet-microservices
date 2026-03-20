# Propagacao de Trace Context no Kafka - Tasks

**Design**: `.specs/features/propagacao-trace-context-kafka/design.md`
**Status**: Completed

---

## Execution Plan

### Phase 1: Nucleo compartilhado e contrato comum

```text
T1 (shared core) -> T2 (facade OrderService)
T1 -> T3 (facade ProcessingWorker)
T1 -> T4 (facade NotificationWorker)
```

### Phase 2: Integracao cirurgica e validacao de build

```text
T2 + T3 + T4 -> T5 (referencias e build)
```

### Phase 3: Revalidacao funcional da baseline de M2

```text
T5 -> T6 (smoke tests Kafka/Postgres) -> T7 (validacao Tempo)
```

---

## Task Breakdown

### T1: Introduzir o nucleo compartilhado de trace context W3C

**What**: Criar o artefato compartilhado com a logica pura de parse e
materializacao de `traceparent` e `tracestate`, sem acoplamento direto ao tipo
`Confluent.Kafka.Headers`.

**Where**: `src/Shared/` ou local equivalente definido na implementacao

**Depends on**: feature `notification-worker-consumer-persistencia` concluida

**Done when**:

- [x] Existe um unico ponto compartilhado para a logica W3C
- [x] O nucleo compartilhado nao depende diretamente de `Confluent.Kafka`
- [x] O parse de `traceparent` invalido retorna ausencia de contexto sem excecao
- [x] A injecao de `tracestate` preserva a semantica atual de no-op quando vazio

**Verification**:

- Local: build da solution passa via SDK 10 em container
- Tempo: nao aplicavel diretamente nesta tarefa

---

### T2: Adaptar o `OrderService` para usar a logica compartilhada

**What**: Trocar a implementacao duplicada do helper local por uma fachada fina
delegando ao nucleo compartilhado, alinhando o contrato para
`Extract(Headers?)` e `Inject(Activity?, Headers)`.

**Where**: `src/OrderService/Messaging/`

**Depends on**: T1

**Done when**:

- [x] `OrderService.Messaging.KafkaTracingHelper` deixa de conter logica W3C duplicada
- [x] O contrato local aceita `Headers?` em `Extract(...)`
- [x] `KafkaOrderPublisher` continua publicando em `orders` sem mudanca de comportamento

**Verification**:

- Local: build do projeto passa
- Tempo: nao aplicavel isoladamente

---

### T3: Adaptar o `ProcessingWorker` para usar a logica compartilhada

**What**: Substituir a logica duplicada do helper do worker por delegacao ao
nucleo compartilhado, preservando o comportamento atual de consume e publish.

**Where**: `src/ProcessingWorker/Messaging/`

**Depends on**: T1

**Done when**:

- [x] `ProcessingWorker.Messaging.KafkaTracingHelper` passa a ser apenas fachada
- [x] `Worker.cs` continua extraindo contexto de `orders` com a mesma semantica atual
- [x] `KafkaNotificationPublisher` continua injetando headers em `notifications`

**Verification**:

- Local: build do projeto passa
- Tempo: nao aplicavel isoladamente

---

### T4: Adaptar o `NotificationWorker` e completar o contrato comum

**What**: Substituir a logica duplicada do helper do `NotificationWorker` por
delegacao ao nucleo compartilhado e adicionar `Inject(Activity?, Headers)` para
fechar o contrato comum, mesmo sem uso imediato no fluxo atual.

**Where**: `src/NotificationWorker/Messaging/`

**Depends on**: T1

**Done when**:

- [x] `NotificationWorker.Messaging.KafkaTracingHelper` passa a ser apenas fachada
- [x] `Extract(Headers?)` preserva o comportamento atual de headers ausentes ou invalidos
- [x] `Inject(Activity?, Headers)` existe como contrato compartilhado e nao interfere no fluxo atual

**Verification**:

- Local: build do projeto passa
- Tempo: nao aplicavel isoladamente

---

### T5: Integrar referencias e validar a solution inteira

**What**: Ligar os tres servicos ao artefato compartilhado escolhido e validar
que a solution continua compilando sem warnings ou erros novos relevantes para a
feature.

**Where**: `otel-poc.sln`, `src/*/*.csproj` e eventuais arquivos do shared core

**Depends on**: T2, T3, T4

**Done when**:

- [x] Os tres projetos resolvem o artefato compartilhado com sucesso
- [x] `dotnet build otel-poc.sln` passa no fluxo validado em container SDK 10
- [x] Nenhum publisher ou worker sofre mudanca de logica alem do helper de propagacao

**Verification**:

- Local: build da solution passa via container SDK 10
- Tempo: nao aplicavel diretamente

---

### T6: Reexecutar smoke tests funcionais da baseline M2

**What**: Revalidar o fluxo feliz e os caminhos degradados mais sensiveis para
garantir que a consolidacao do helper nao alterou o comportamento observavel.

**Where**: ambiente Docker Compose ja usado na baseline

**Depends on**: T5

**Done when**:

- [x] Um `POST /orders` continua gerando mensagem em `orders` com `traceparent`
- [x] O `ProcessingWorker` continua publicando em `notifications` com `traceparent`
- [x] O `NotificationWorker` continua persistindo `traceId` em `notification_results`
- [x] Mensagens validas sem headers W3C continuam sendo tratadas com novo trace local

**Verification**:

- Local: logs, consumo Kafka e consulta ao PostgreSQL confirmam o comportamento esperado
- Tempo: nao e o foco principal desta tarefa

---

### T7: Validar ausencia de regressao no Tempo

**What**: Confirmar que o trace distribuido continua identico, do ponto de vista
observavel, ao fluxo ja validado antes da refatoracao.

**Where**: Grafana Tempo e artefatos locais de suporte

**Depends on**: T6

**Done when**:

- [x] O caminho feliz continua mostrando `POST /orders` -> `kafka publish orders` -> `kafka consume orders` -> `GET /orders/{id}` -> `kafka publish notifications` -> `kafka consume notifications` -> span DB
- [x] O `TraceId` persistido em `notification_results` coincide com o exibido no Tempo
- [x] O caminho degradado sem headers W3C no `ProcessingWorker` continua iniciando novo trace e publicando normalmente
- [x] O caminho degradado sem headers W3C no `NotificationWorker` continua iniciando novo trace e persistindo normalmente

**Verification**:

- Local: consulta ao Kafka e ao PostgreSQL confirma correlacao basica
- Tempo: busca por `TraceId` e por spans conhecidos confirma ausencia de drift

---

## Validation Notes

- O host local continua sem .NET 10 SDK; o build deve ser validado via container Docker com `mcr.microsoft.com/dotnet/sdk:10.0`
- Esta feature deve reaproveitar os cenarios de validacao ja registrados em `STATE.md`, sem inventar novos caminhos de negocio
- Qualquer diferenca de spans, headers ou logs em relacao a baseline de M2 deve ser tratada como regressao ate prova em contrario
- O build da solution passou com `docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build otel-poc.sln` apos a extracao do nucleo compartilhado.
- O primeiro `docker compose up -d --build` expôs uma regressao de build por falta de `src/Shared/` nos Dockerfiles; o problema foi corrigido adicionando a copia explicita dessa pasta nos tres servicos.
- O fluxo feliz foi revalidado com `POST /orders`, confirmacao de `traceparent` nos topics `orders` e `notifications`, persistencia em `notification_results` e consulta ao Tempo pelo trace id `085c91ec7c3a83fc13f7ca7a835960be`.
- O caminho degradado do `ProcessingWorker` foi revalidado com evento manual sem headers em `orders`, warning estruturado, novo trace local `0d1ff182b3756e771ae7e5bcb9687141`, publish normal em `notifications` e correlacao final no PostgreSQL e no Tempo.
- O caminho degradado do `NotificationWorker` foi revalidado com evento manual sem headers em `notifications`, warning estruturado, novo trace local `ce75c561d983cae17a4d478b2b8b08b2`, persistencia bem-sucedida e presenca de `kafka consume notifications` + span DB no Tempo.

---

## Parallel Execution Map

```text
Phase 1:
  T1 -> T2
  T1 -> T3
  T1 -> T4

Phase 2:
  T2 + T3 + T4 -> T5

Phase 3:
  T5 -> T6 -> T7
```