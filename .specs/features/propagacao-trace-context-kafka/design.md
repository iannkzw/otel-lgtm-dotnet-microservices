# Propagacao de Trace Context no Kafka - Design

**Spec**: `.specs/features/propagacao-trace-context-kafka/spec.md`
**Status**: Designed

---

## Architecture Overview

A feature deve consolidar a propagacao W3C do Kafka sem reabrir o fluxo ja
validado de M2. O desenho mais seguro para isso e separar a refatoracao em dois
niveis:

1. um nucleo compartilhado e pequeno, responsavel apenas pela logica W3C pura;
2. adaptadores Kafka-facing minimos em cada servico, preservando o contrato
   `Extract(Headers?)` e `Inject(Activity?, Headers)` onde ele ja e usado.

Essa divisao reduz a duplicacao funcional sem obrigar uma migracao ampla de
namespaces, sem acoplar a solucao a uma unica versao de `Confluent.Kafka` e sem
alterar publishers, consumers, spans ou payloads alem do necessario.

Fluxo arquitetural esperado apos a implementacao:

1. `OrderService`, `ProcessingWorker` e `NotificationWorker` passam a reutilizar o mesmo nucleo W3C.
2. Cada servico mantem um `KafkaTracingHelper` fino como fachada de compatibilidade ou migra para um alias equivalente de baixo churn.
3. Os call sites existentes continuam chamando `Extract(...)` e `Inject(...)` no ponto mais proximo do carrier Kafka.
4. Os spans manuais e a logica de negocio dos workers/publishers permanecem inalterados.

---

## Design Decisions

### Usar um nucleo compartilhado sem dependencia direta de Kafka

**Decision**: Introduzir um artefato compartilhado centrado na logica W3C pura,
sem depender diretamente de `Confluent.Kafka.Headers`.

**Reason**: A baseline atual usa `Confluent.Kafka 2.11.0` no `OrderService` e
`2.5.0` nos workers. Um helper compartilhado que dependesse diretamente do tipo
Kafka tenderia a puxar uma convergencia de versao desnecessaria para esta
iteracao.

**Trade-off**: Continua existindo uma camada fina de adaptacao local para ler e
escrever `Headers`, mas a duplicacao relevante fica concentrada em um unico
artefato compartilhado.

### Preservar uma fachada local `KafkaTracingHelper` durante a migracao

**Decision**: Manter o nome `KafkaTracingHelper` como fachada local minima em
cada servico durante a primeira iteracao da consolidacao.

**Reason**: Os call sites atuais ja estao validados e distribuidos em publishers
e workers. Preservar a fachada reduz churn, evita edicoes amplas em namespaces e
mantem o diff concentrado no helper.

**Trade-off**: Ainda havera um arquivo por servico, mas ele deixara de conter
duplicacao funcional e passara a ser apenas compatibilidade de borda.

### Padronizar o contrato para `Extract(Headers?)` e `Inject(Activity?, Headers)`

**Decision**: O contrato Kafka-facing sera o mesmo nos tres servicos,
independentemente de cada um usar hoje apenas o lado producer, apenas o lado
consumer ou ambos.

**Reason**: Essa consistencia reduz drift futuro e facilita evolucoes sem nova
discussao de assinatura por servico.

**Trade-off**: Alguns metodos permanecerao sem uso imediato em um dos servicos,
mas isso e aceitavel como contrato minimo compartilhado.

### Nao alterar os pontos de criacao de spans nem o ciclo de vida dos headers

**Decision**: A implementacao da feature nao deve mover a abertura dos spans
manuais nem alterar o fato de os publishers atuais criarem `new Headers()` antes
da injecao.

**Reason**: O objetivo e isolar a refatoracao ao helper de propagacao, nao ao
pipeline observavel do fluxo.

**Trade-off**: O helper compartilhado continua assumindo a semantica atual de
carriers novos no publish, em vez de introduzir regras extras para carriers
reutilizados.

---

## Proposed Shared Structure

### Shared W3C Core

- **Purpose**: Centralizar parse e materializacao de `traceparent` e
  `tracestate` sem conhecimento de Kafka
- **Suggested location**: `src/Shared/KafkaTraceContext/` ou equivalente
- **Suggested responsibility split**:
  - normalizar valores de `traceparent` e `tracestate`
  - executar `ActivityContext.TryParse(...)`
  - materializar o conjunto de headers W3C a partir de uma `Activity`

### Local Kafka Facade Per Service

- **Purpose**: Adaptar `Confluent.Kafka.Headers` para o nucleo compartilhado
- **Locations**:
  - `src/OrderService/Messaging/KafkaTracingHelper.cs`
  - `src/ProcessingWorker/Messaging/KafkaTracingHelper.cs`
  - `src/NotificationWorker/Messaging/KafkaTracingHelper.cs`
- **Responsibilities**:
  - manter assinatura `Extract(Headers?)`
  - manter assinatura `Inject(Activity?, Headers)`
  - iterar os headers com busca case-insensitive
  - sobrescrever headers via `Remove(key)` + `Add(...)` como na baseline atual

### Untouched Business Call Sites

- `src/OrderService/Messaging/KafkaOrderPublisher.cs`
- `src/ProcessingWorker/Worker.cs`
- `src/ProcessingWorker/Messaging/KafkaNotificationPublisher.cs`
- `src/NotificationWorker/Worker.cs`

Esses pontos devem sofrer, no maximo, ajustes mecanicos de namespace se a
fachada local nao for mantida exatamente onde esta. Nao deve haver mudanca de
logica de spans, logging, topicos ou payloads.

---

## Contract Design

### Kafka-facing facade

```csharp
public static ActivityContext? Extract(Headers? headers)
public static void Inject(Activity? activity, Headers headers)
```

### Shared core

O nucleo compartilhado pode assumir uma forma pura, sem conhecer Kafka. Uma
opcao segura e expor operacoes equivalentes a:

```csharp
public static ActivityContext? Extract(string? traceParent, string? traceState)
public static void Inject(Activity? activity, Action<string, string> setHeader)
```

ou um shape funcional equivalente, desde que concentre a logica W3C em um unico
ponto e deixe aos adaptadores locais apenas a traducao entre `Headers` e string.

### Header semantics to preserve

- `traceparent` continua obrigatorio para continuidade do trace
- `tracestate` continua opcional
- busca de header continua case-insensitive
- `Inject(...)` continua sendo no-op para `activity = null` ou `activity.Id` em branco
- `Extract(...)` continua retornando `null` para parse invalido ou contexto ausente

---

## Migration Strategy

### Step 1: Introduzir o nucleo compartilhado sem tocar no fluxo de negocio

Criar o artefato compartilhado com a logica W3C pura e cobertura minima de build.
Nenhum publisher, worker, span ou configuracao operacional deve ser alterado
nesta etapa alem do necessario para referenciar o novo nucleo.

### Step 2: Adaptar o `KafkaTracingHelper` de cada servico para fachada fina

- `OrderService`: alinhar `Extract` para aceitar `Headers?` e delegar ao nucleo compartilhado
- `ProcessingWorker`: manter o contrato atual, mas trocar a implementacao por delegacao
- `NotificationWorker`: adicionar `Inject(Activity?, Headers)` e trocar `Extract(...)` por delegacao

### Step 3: Preservar call sites e comportamento observavel

Manter exatamente os mesmos pontos de chamada:

- `KafkaOrderPublisher.PublishAsync(...)`
- `ProcessingWorker.Worker.ProcessMessageAsync(...)`
- `KafkaNotificationPublisher.PublishAsync(...)`
- `NotificationWorker.Worker.ProcessMessageAsync(...)`

Qualquer alteracao adicional nesses arquivos aumenta risco sem ganho direto.

### Step 4: Revalidar a baseline de M2

Executar build da solution e repetir os checks minimos ja registrados em
`STATE.md`:

1. fluxo feliz `POST /orders` -> `notification_results`
2. inspecao de `traceparent` nos topicos `orders` e `notifications`
3. validacao do trace unico no Tempo
4. caminho degradado com headers ausentes em `ProcessingWorker`
5. caminho degradado com headers ausentes em `NotificationWorker`

---

## Regression Risks

### Risk 1: Drift de semantica por convergencia involuntaria de dependencia Kafka

Se a consolidacao for feita em um projeto compartilhado dependente de
`Confluent.Kafka`, a feature pode forcar alinhamento de versoes antes da hora e
abrir risco de regressao fora do helper.

**Mitigation**: manter o nucleo compartilhado livre de dependencia direta de
Kafka e deixar `Headers` apenas nas fachadas locais.

### Risk 2: Alterar spans ou logs ao tocar os call sites

O helper esta muito proximo dos pontos que abrem spans e registram logs. Uma
refatoracao ampla nesses arquivos pode mudar comportamento observavel no Tempo.

**Mitigation**: limitar os edits de negocio a ajustes mecanicos e preservar
integralmente nomes de spans, tags, topicos e mensagens de log.

### Risk 3: Mudar a semantica atual de `tracestate`

A baseline atual so injeta `tracestate` quando `Activity.TraceStateString` nao
esta em branco. Uma implementacao nova que remova ou manipule o header de outra
forma pode introduzir regressao sutil.

**Mitigation**: manter exatamente a mesma regra de injecao ja validada.

---

## Validation Plan

### Build validation

- `docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build otel-poc.sln`

### Functional validation

1. Subir o ambiente com `docker compose up -d --build`
2. Criar pedido real via `POST /orders`
3. Confirmar `traceparent` no topic `orders`
4. Confirmar `traceparent` no topic `notifications`
5. Confirmar persistencia final em `notification_results`

### Tempo validation

Confirmar um unico trace contendo, no minimo:

1. `POST /orders`
2. `kafka publish orders`
3. `kafka consume orders`
4. `GET /orders/{id}`
5. `kafka publish notifications`
6. `kafka consume notifications`
7. span de banco do `NotificationWorker`

### Degraded validation

- mensagem valida sem headers W3C em `orders` continua processando com novo trace no `ProcessingWorker`
- mensagem valida sem headers W3C em `notifications` continua persistindo com novo trace no `NotificationWorker`

---

## Implementation Notes

- A feature deve ser tratada como refatoracao de infraestrutura, nao como alteracao de comportamento
- A prioridade e reduzir duplicacao funcional, nao eliminar toda e qualquer fachada local em uma unica iteracao
- Se houver conflito entre elegancia estrutural e preservacao da baseline validada, a baseline validada deve vencer