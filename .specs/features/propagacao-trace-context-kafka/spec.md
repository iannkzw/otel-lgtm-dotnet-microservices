# Propagacao de Trace Context no Kafka - Specification

**Milestone**: M2 - Fluxo de Eventos End-to-End
**Status**: Specified

---

## Problem Statement

O fluxo de M2 ja foi validado ponta a ponta no Tempo com propagacao manual de
`traceparent` e `tracestate` entre `OrderService`, `ProcessingWorker` e
`NotificationWorker`. Porem, a logica W3C hoje esta duplicada em tres arquivos
`KafkaTracingHelper`, com pequenas divergencias de contrato:

- `OrderService` expoe `Inject(Activity?, Headers)` e `Extract(Headers)`
- `ProcessingWorker` expoe `Inject(Activity?, Headers)` e `Extract(Headers?)`
- `NotificationWorker` expoe apenas `Extract(Headers?)`

Essa duplicacao aumenta o risco de drift funcional justamente no trecho mais
sensivel do trace distribuido de M2. A feature precisa consolidar a propagacao
W3C em um helper compartilhado, reduzir a duplicacao e padronizar o contrato
minimo sem alterar os topicos Kafka, os payloads, os nomes de spans ou o
comportamento ja validado no Tempo.

## Goals

- [ ] Consolidar a logica W3C de Kafka em um artefato compartilhado entre os 3 servicos
- [ ] Padronizar o contrato minimo `Extract(Headers?)` e `Inject(Activity?, Headers)`
- [ ] Preservar o comportamento atual de `traceparent` e `tracestate` no fluxo feliz e degradado
- [ ] Definir uma estrategia de migracao cirurgica que reduza churn nos call sites existentes
- [ ] Evitar regressao em spans manuais, logs estruturados, topicos e contratos Kafka ja estabilizados em M2
- [ ] Definir criterios objetivos de validacao local e no Tempo para provar ausencia de regressao funcional

## Out of Scope

- Alterar os payloads publicados em `orders` ou `notifications`
- Renomear spans ja estabilizados: `kafka publish orders`, `kafka consume orders`, `kafka publish notifications`, `kafka consume notifications`
- Introduzir instrumentacao automatica de Kafka alem do que ja existe
- Reabrir as features de persistencia, enriquecimento HTTP ou observabilidade de M2
- Alinhar versoes de `Confluent.Kafka` entre todos os projetos como precondicao da refatoracao
- Adicionar retry, DLQ, outbox ou qualquer mudanca de semantica de entrega

---

## Current Baseline

### OrderService

- Usa `KafkaTracingHelper.Inject(Activity.Current, message.Headers)` no publish para `orders`
- Cria `Headers = new Headers()` antes da injecao
- Mantem o span manual `kafka publish orders`

### ProcessingWorker

- Usa `KafkaTracingHelper.Extract(consumeResult.Message.Headers)` no consume de `orders`
- Usa `KafkaTracingHelper.Inject(Activity.Current, message.Headers)` no publish para `notifications`
- Trata headers ausentes ou invalidos como ausencia de contexto distribuido e inicia novo trace
- Mantem os spans manuais `kafka consume orders` e `kafka publish notifications`

### NotificationWorker

- Usa `KafkaTracingHelper.Extract(consumeResult.Message.Headers)` no consume de `notifications`
- Trata headers ausentes ou invalidos como ausencia de contexto distribuido e inicia novo trace
- Mantem o span manual `kafka consume notifications`

---

## Shared Contract

O contrato compartilhado desta feature deve permanecer minimo e orientado ao uso
real ja consolidado no codigo:

### `ActivityContext? Extract(Headers? headers)`

**Required behavior**:

1. WHEN `headers` for `null` ou vazio THEN o metodo SHALL retornar `null`
2. WHEN `traceparent` estiver ausente ou em branco THEN o metodo SHALL retornar `null`
3. WHEN `traceparent` for invalido para `ActivityContext.TryParse(...)` THEN o metodo SHALL retornar `null` sem lancar excecao
4. WHEN `tracestate` estiver presente THEN o metodo SHALL repassa-lo para o parse do contexto
5. WHEN a chave de header variar em maiusculas/minusculas THEN a busca SHALL permanecer case-insensitive
6. WHEN o parse for bem-sucedido THEN o metodo SHALL retornar `ActivityContext` pronto para ser usado como parent dos spans de consume

### `void Inject(Activity? activity, Headers headers)`

**Required behavior**:

1. WHEN `activity` for `null` THEN o metodo SHALL ser no-op
2. WHEN `activity.Id` estiver nulo, vazio ou em branco THEN o metodo SHALL ser no-op
3. WHEN houver contexto ativo valido THEN o metodo SHALL sobrescrever `traceparent` no carrier informado
4. WHEN `activity.TraceStateString` existir e nao estiver em branco THEN o metodo SHALL sobrescrever `tracestate`
5. WHEN `activity.TraceStateString` estiver ausente ou em branco THEN o metodo SHALL preservar a semantica atual e nao introduzir manipulacao adicional de `tracestate`

**Operational note**:

- Os publishers atuais constroem `Headers` novos antes da injecao. A implementacao desta feature nao deve assumir carriers reutilizados nem ampliar a semantica alem do comportamento ja validado em M2.

---

## User Stories

### P1: Compartilhar a logica W3C sem mudar o comportamento observavel do fluxo ⭐ MVP

**User Story**: Como pessoa mantenedora da PoC, quero centralizar a logica de
propagacao W3C do Kafka em um helper compartilhado, para reduzir duplicacao sem
quebrar o trace distribuido ja validado no Tempo.

**Why P1**: O valor desta feature esta em eliminar drift futuro sem reabrir o
comportamento de M2.

**Acceptance Criteria**:

1. WHEN a feature for implementada THEN os tres servicos SHALL depender da mesma logica compartilhada para parse e injecao W3C
2. WHEN a migracao terminar THEN os topicos `orders` e `notifications` SHALL permanecer inalterados
3. WHEN a migracao terminar THEN os nomes de spans manuais SHALL permanecer exatamente iguais aos da baseline validada
4. WHEN o fluxo feliz for executado apos a migracao THEN o `TraceId` SHALL continuar unico entre os tres servicos
5. WHEN houver headers ausentes ou invalidos THEN o comportamento degradado SHALL permanecer equivalente ao atual

**Independent Test**: Executar um `POST /orders`, confirmar continuidade do trace no Tempo e inspecionar os headers dos eventos Kafka para provar que `traceparent` continua presente e parseavel.

---

### P1: Padronizar o contrato Kafka-facing entre os tres servicos ⭐ MVP

**User Story**: Como pessoa desenvolvedora da PoC, quero que os tres servicos
enxerguem o mesmo contrato minimo de helper Kafka, para evitar variacoes de
assinatura entre producer e consumer.

**Why P1**: Hoje ha divergencia entre `Extract(Headers)` e `Extract(Headers?)`,
alem da ausencia de `Inject(...)` no `NotificationWorker`.

**Acceptance Criteria**:

1. WHEN a feature for concluida THEN `OrderService`, `ProcessingWorker` e `NotificationWorker` SHALL expor `Extract(Headers?)`
2. WHEN a feature for concluida THEN `OrderService`, `ProcessingWorker` e `NotificationWorker` SHALL expor `Inject(Activity?, Headers)`
3. WHEN um servico nao usar um dos lados do contrato no fluxo atual THEN a assinatura ainda SHALL existir para manter consistencia minima compartilhada
4. WHEN o helper for chamado com `headers = null` no caminho de extract THEN o retorno SHALL ser `null` sem excecao
5. WHEN o helper for chamado com `activity = null` no caminho de inject THEN o carrier SHALL permanecer inalterado

**Independent Test**: Compilar a solution e validar que os call sites atuais continuam funcionais sem mudanca de semantica.

---

### P1: Migrar de forma cirurgica, preservando call sites e namespaces onde fizer sentido ⭐ MVP

**User Story**: Como pessoa responsavel pela baseline de M2, quero uma migracao
de baixo impacto, para reduzir risco de regressao em spans, logs e headers ja
observados.

**Why P1**: Uma refatoracao estrutural ampla aqui teria custo desproporcional ao
beneficio imediato.

**Acceptance Criteria**:

1. WHEN a implementacao tocar publishers e workers existentes THEN ela SHALL evitar reescrever a logica de negocio ao redor dos helpers
2. WHEN a implementacao introduzir um artefato compartilhado THEN ele SHALL concentrar apenas a logica de propagacao W3C, sem absorver responsabilidades de publish, consume ou logging
3. WHEN a migracao terminar THEN qualquer adaptacao local restante SHALL ser fina o bastante para ser reconhecida como compatibilidade, nao como nova duplicacao funcional
4. WHEN a implementacao exigir alteracao estrutural THEN ela SHALL explicitar por que essa alteracao e a opcao de menor risco para a baseline atual

**Independent Test**: Revisar o diff final e confirmar que as mudancas se concentram no helper compartilhado e em adaptacoes locais minimas, sem churn amplo nos publishers/workers.

---

### P2: Tornar a ausencia de regressao verificavel no Tempo e no Kafka

**User Story**: Como operador da PoC, quero criterios claros para validar a
refatoracao, para saber que o trace distribuido continua correto apos a
consolidacao do helper.

**Why P2**: A refatoracao so tem valor se continuar invisivel do ponto de vista
do comportamento observavel.

**Acceptance Criteria**:

1. WHEN o fluxo feliz for executado THEN o Tempo SHALL continuar exibindo um unico trace com os spans manuais ja conhecidos dos tres servicos
2. WHEN uma mensagem sem headers W3C for consumida no `ProcessingWorker` THEN o worker SHALL continuar iniciando novo trace local e publicando normalmente em `notifications`
3. WHEN uma mensagem sem headers W3C for consumida no `NotificationWorker` THEN o worker SHALL continuar iniciando novo trace local e persistindo normalmente quando o payload for valido
4. WHEN um evento Kafka for inspecionado apos o publish THEN `traceparent` SHALL continuar presente e `tracestate` SHALL continuar opcional

**Independent Test**: Reexecutar os cenarios feliz e degradado ja validados em M2 e comparar spans, headers e logs com a baseline registrada em `STATE.md`.

## Edge Cases

- WHEN um servico passar `Headers? = null` para `Extract(...)` THEN o helper SHALL retornar `null`
- WHEN `traceparent` vier duplicado em diferentes combinacoes de caixa THEN a busca SHALL continuar usando comparacao case-insensitive e manter a primeira correspondencia efetiva encontrada pelo algoritmo padrao adotado
- WHEN `tracestate` vier ausente THEN o parse do contexto SHALL continuar funcionando apenas com `traceparent`
- WHEN `activity.Id` nao puder ser usado como valor W3C THEN `Inject(...)` SHALL falhar de forma silenciosa, como no comportamento atual
- WHEN `NotificationWorker` nao usar `Inject(...)` no fluxo atual THEN a existencia do metodo SHALL ser tratada como contrato de consistencia, nao como obrigacao de uso imediato

## Validation Criteria

### Happy path

1. Executar `POST /orders`
2. Confirmar que o evento em `orders` continua contendo `traceparent`
3. Confirmar no Tempo um unico trace contendo:
   - `POST /orders`
   - `kafka publish orders`
   - `kafka consume orders`
   - `GET /orders/{id}`
   - `kafka publish notifications`
   - `kafka consume notifications`
   - span de banco do `NotificationWorker`
4. Confirmar que o `TraceId` persistido em `notification_results` continua igual ao exibido no Tempo

### Degraded path: headers ausentes no ProcessingWorker

1. Produzir mensagem valida em `orders` sem `traceparent`
2. Confirmar warning de contexto ausente ou invalido no `ProcessingWorker`
3. Confirmar novo trace iniciado localmente e publish normal em `notifications`

### Degraded path: headers ausentes no NotificationWorker

1. Produzir mensagem valida em `notifications` sem `traceparent`
2. Confirmar warning de contexto ausente ou invalido no `NotificationWorker`
3. Confirmar persistencia bem-sucedida com novo `TraceId` local

## Success Criteria

- [ ] A logica W3C de Kafka passa a existir em um artefato compartilhado, sem duplicacao funcional relevante entre os servicos
- [ ] O contrato minimo `Extract(Headers?)` e `Inject(Activity?, Headers)` fica consistente nos tres servicos
- [ ] Os spans, topicos, payloads e logs ja validados em M2 permanecem inalterados em semantica
- [ ] O fluxo feliz e os caminhos degradados continuam reproduziveis com o mesmo comportamento observavel registrado em `STATE.md`