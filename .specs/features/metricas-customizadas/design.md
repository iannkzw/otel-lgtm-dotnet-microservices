# Metricas Customizadas - Design

**Spec**: `.specs/features/metricas-customizadas/spec.md`
**Status**: Designed

---

## Architecture Overview

Esta feature adiciona instrumentacao de metricas customizadas aos tres servicos
da PoC sem alterar o fluxo funcional ja validado em M2. O desenho proposto
segue tres principios:

1. o bootstrap OpenTelemetry continua centralizado em `AddOtelInstrumentation()`
   de cada servico;
2. a emissao de counters e histograms fica encapsulada em um recorder local por
   servico, para evitar strings e labels espalhadas pelo codigo;
3. os gauges leem apenas snapshots em memoria, atualizados fora do callback, para
   impedir consultas pesadas a banco ou broker no caminho de exportacao.

Arquitetura alvo por servico:

1. `OtelExtensions` passa a registrar tracing e metrics usando o mesmo
   `service.name` e o mesmo endpoint OTLP ja existente.
2. Um recorder dedicado centraliza `Meter`, instrumentos, nomes canonicos e
   helpers de labels baixas em cardinalidade.
3. O fluxo de negocio chama apenas metodos pequenos como `RecordCreateResult(...)`,
   `RecordProcessingResult(...)` ou `RecordPersistenceResult(...)` nos pontos onde
   o resultado ja esta definido.
4. Backlog e lag sao mantidos em snapshots singleton atualizados por refresh
   bounded, e o `ObservableGauge` apenas le esses valores.

Fluxo de alto nivel:

1. O `OrderService` registra o desfecho de `POST /orders` e expoe backlog agregado
   por status interno.
2. O `ProcessingWorker` registra o desfecho do processamento por mensagem,
   inclusive enriquecimento HTTP e publish em `notifications`, e expoe lag
   agregado do consumer group.
3. O `NotificationWorker` registra o desfecho do consumo/persistencia e expoe o
   mesmo padrao de lag agregado.
4. Todos os datapoints seguem o caminho `service -> otelcol -> lgtm`, sem novo
   endpoint Prometheus nos servicos.

---

## Design Decisions

### Manter recorder e meter por servico, sem shared metrics library agora

**Decision**: Cada servico tera sua propria classe de recorder de metricas,
com `Meter`, instrumentos e contratos pequenos, em vez de uma biblioteca
compartilhada entre API e workers.

**Reason**: Os instrumentos, labels, dependencias e pontos de emissao sao
diferentes entre os tres servicos. A tentativa de unificacao agora aumentaria o
acoplamento sem ganho claro.

**Trade-off**: Havera padrao estrutural repetido entre as implementacoes, mas com
churn menor e responsabilidade mais clara por servico.

### Estender apenas o bootstrap existente de OTel

**Decision**: `AddOtelInstrumentation()` continuara sendo o unico ponto de
entrada para a configuracao OpenTelemetry em cada servico, agora adicionando
`WithMetrics(...)` ao lado de `WithTracing(...)`.

**Reason**: O repositorio ja consolidou esse bootstrap como fronteira de
observabilidade; manter a integracao ali reduz dispersao de configuracao.

**Trade-off**: O builder passa a acumular responsabilidades de traces e metrics,
mas isso ja e o padrao atual da PoC para observabilidade.

### Gauges baseados em snapshot, nunca em callback pesado

**Decision**: Os `ObservableGauge` vao ler estado cacheado em memoria. O refresh
desse estado acontecera fora do callback, com consultas bounded a banco ou broker.

**Reason**: O callback de `ObservableGauge` deve ser leve e previsivel. Fazer IO
nesse ponto aumentaria risco de travamento, atrasos de coleta e falhas dificeis
de diagnosticar.

**Trade-off**: Os valores publicados podem ter alguns segundos de defasagem em
relacao ao estado exato do sistema.

### Limitar labels a enums/constantes de resultado

**Decision**: O codigo nao deve construir labels dinamicamente a partir de
payload, IDs ou mensagens de erro. Os valores de `result`, `status`, `topic` e
`consumer_group` devem vir de conjuntos fechados e previsiveis.

**Reason**: Isso preserva cardinalidade baixa e deixa as queries de validacao e
dashboard sustentaveis no backend.

**Trade-off**: O diagnostico fino continua sendo responsabilidade de traces e logs.

### Validacao deve separar nome canonico do instrumento e nome normalizado no backend

**Decision**: O codigo vai usar os nomes canonicos definidos na spec, mas a
validacao precisa aceitar a forma normalizada do backend Prometheus, tipicamente
com underscores no Explore.

**Reason**: A feature trabalha com nomenclatura OpenTelemetry no codigo, mas o
LGTM pode apresentar uma view Prometheus-compatível na consulta.

**Trade-off**: As instructions de validacao precisam listar as duas formas quando
isso for relevante.

---

## Proposed File Layout

### OrderService

- `src/OrderService/Extensions/OtelExtensions.cs`
- `src/OrderService/Metrics/OrderMetrics.cs`
- `src/OrderService/Metrics/OrderBacklogSnapshot.cs`
- `src/OrderService/Metrics/OrderBacklogSampler.cs`

### ProcessingWorker

- `src/ProcessingWorker/Extensions/OtelExtensions.cs`
- `src/ProcessingWorker/Metrics/ProcessingMetrics.cs`
- `src/ProcessingWorker/Metrics/KafkaLagSnapshot.cs`
- `src/ProcessingWorker/Metrics/ProcessingLagRefresher.cs`

### NotificationWorker

- `src/NotificationWorker/Extensions/OtelExtensions.cs`
- `src/NotificationWorker/Metrics/NotificationMetrics.cs`
- `src/NotificationWorker/Metrics/KafkaLagSnapshot.cs`
- `src/NotificationWorker/Metrics/NotificationLagRefresher.cs`

O design evita tocar `src/Shared/` nesta iteracao. As metricas sao
especificas demais por servico para justificar um novo artefato compartilhado.

---

## Shared Design Pattern Across Services

Cada recorder de metricas deve concentrar:

1. nome do `Meter`;
2. nomes canonicos dos instrumentos;
3. criacao de `Counter<long>`, `Histogram<double>` e `ObservableGauge<long>`;
4. helpers para emitir measurements com labels fechadas;
5. interfaces pequenas para o codigo de negocio.

Forma conceitual:

```csharp
public interface IOrderMetrics
{
    ValueStopwatch StartCreateTimer();
    void RecordCreateResult(string result, double durationMs);
}
```

O codigo final nao precisa usar exatamente essa assinatura, mas deve preservar o
principio de que o handler/worker informa apenas o resultado final e a duracao,
enquanto o recorder encapsula nomes de metricas e labels.

---

## Bootstrap Strategy

## OrderService

`OrderService.Extensions.OtelExtensions` deve evoluir de:

- `WithTracing(...)`

para:

- `WithTracing(...)`
- `WithMetrics(...)`

`WithMetrics(...)` deve:

1. usar o mesmo `ResourceBuilder.CreateDefault().AddService(serviceName, ...)`;
2. registrar apenas o meter customizado do `OrderService` nesta feature;
3. configurar `AddOtlpExporter(...)` com o mesmo endpoint e protocolo gRPC;
4. nao abrir escopo de runtime metrics, process metrics ou instrumentacao extra
   nao prevista na spec.

## ProcessingWorker

Mesmo desenho do `OrderService`, registrando apenas o meter customizado do
worker e reutilizando `OTEL_EXPORTER_OTLP_ENDPOINT` ja existente.

## NotificationWorker

Mesmo desenho do `ProcessingWorker`, mantendo os spans e a instrumentacao de EF
Core/Http ja consolidados para tracing.

---

## OrderService Design

## Components

### `OrderMetrics`

- **Purpose**: centralizar `orders.created.total`, `orders.create.duration` e
  `orders.backlog.current`
- **Dependencies**: `OrderBacklogSnapshot`
- **Responsibilities**:
  - criar os instrumentos do meter;
  - expor helper para registrar resultado do endpoint;
  - expor gauge a partir do snapshot agregado.

### `OrderBacklogSnapshot`

- **Purpose**: armazenar em memoria o ultimo valor conhecido de backlog por status
- **Shape sugerido**:
  - `PendingPublishCount`
  - `PublishFailedCount`
  - `LastUpdatedUtc`

### `OrderBacklogSampler`

- **Purpose**: atualizar o snapshot de backlog em intervalo fixo e bounded
- **Dependencies**: `IServiceScopeFactory`, `OrderDbContext`, logger,
  configuracao opcional de intervalo
- **Responsibilities**:
  - abrir scope periodicamente;
  - executar query agregada por status, sem iterar por pedido;
  - atualizar o snapshot singleton;
  - absorver falhas sem quebrar a API.

## Integration Points

### `Program.cs`

No handler de `POST /orders`:

1. iniciar um timer logo no inicio do endpoint;
2. manter uma variavel local `result` com valor default seguro;
3. definir `result` em cada caminho de retorno:
   - `validation_failed`
   - `persist_failed`
   - `publish_failed`
   - `status_update_failed`
   - `created`
4. registrar counter + histogram uma unica vez imediatamente antes do retorno ou
   em bloco `finally` equivalente.

### Gauge de backlog

O gauge nao deve consultar o banco no callback. O callback deve apenas emitir:

1. `orders.backlog.current{status="pending_publish"}` com o valor do snapshot;
2. `orders.backlog.current{status="publish_failed"}` com o valor do snapshot.

## Query Design

O sampler deve usar query agregada de contagem, conceitualmente equivalente a:

```sql
select status, count(*)
from orders
where status in ('pending_publish', 'publish_failed')
group by status;
```

Isso evita varredura orientada a entidade e mantem o custo previsivel.

---

## ProcessingWorker Design

## Components

### `ProcessingMetrics`

- **Purpose**: centralizar `orders.processed.total`,
  `orders.processing.duration` e `kafka.consumer.lag`
- **Dependencies**: `KafkaLagSnapshot`
- **Responsibilities**:
  - registrar resultados agregados do processamento;
  - registrar duracao total por mensagem;
  - expor gauge de lag a partir do snapshot.

### `KafkaLagSnapshot`

- **Purpose**: guardar o ultimo lag agregado conhecido do worker
- **Shape sugerido**:
  - `Topic`
  - `ConsumerGroup`
  - `Lag`
  - `LastUpdatedUtc`

### `ProcessingLagRefresher`

- **Purpose**: atualizar o snapshot de lag fora do callback do gauge
- **Dependencies**: consumer/adaptador de lag, configuracao do topic/group,
  logger
- **Responsibilities**:
  - executar refresh em ritmo bounded;
  - calcular lag agregado do topic/grupo;
  - tratar ausencia de particoes atribuídas sem falhar o host.

## Lag Refresh Strategy

O design preferido e:

1. manter o `ObservableGauge` somente como leitor do snapshot;
2. atualizar o snapshot por um refresher dedicado, em intervalo fixo;
3. calcular lag como soma agregada do atraso das particoes atribuídas ao grupo;
4. publicar apenas o total agregado por `topic` e `consumer_group`.

Requisito operacional:

- o refresh de lag deve ser bounded por timeout curto;
- falha temporaria no broker nao pode interromper o consumo de mensagens;
- se nao houver atribuicao ou valor confiavel no momento, o refresher pode
  manter o ultimo valor conhecido ou publicar `0`, desde que a escolha seja
  consistente na implementacao.

## Integration Points

### `Worker.cs`

O worker deve receber o recorder de metricas por DI.

Dentro de `ProcessMessageAsync(...)`:

1. iniciar timer no comeco do processamento da mensagem;
2. manter variavel local `result` com default `unexpected_error`;
3. atualizar `result` nos caminhos abaixo:
   - `invalid_payload`
   - `not_found`
   - `http_error`
   - `timeout`
   - `network_error`
   - `publish_failed`
   - `processed`
4. registrar counter + histogram ao final do processamento com o `result`
   consolidado.

No catch externo do loop por mensagem:

1. registrar `unexpected_error` exatamente uma vez;
2. evitar duplicidade com o caminho interno de `ProcessMessageAsync(...)`.

## Interaction with OrderServiceClient

O `OrderServiceClient` continua encapsulando a classificacao do resultado HTTP.
O design nao move metricas para o client; a responsabilidade de gravar metricas
permanece no worker, onde o resultado final da mensagem e conhecido.

---

## NotificationWorker Design

## Components

### `NotificationMetrics`

- **Purpose**: centralizar `notifications.persisted.total`,
  `notifications.persistence.duration` e `kafka.consumer.lag`
- **Dependencies**: `KafkaLagSnapshot`
- **Responsibilities**:
  - registrar desfecho agregado do processamento;
  - registrar duracao da tentativa de persistencia;
  - expor gauge de lag a partir do snapshot.

### `NotificationLagRefresher`

- **Purpose**: mesmo padrao do `ProcessingLagRefresher`, mas para topic
  `notifications` e group `notification-worker`

## Integration Points

### `Worker.cs`

O fluxo deve separar duas medidas de tempo:

1. duracao total da mensagem, usada apenas para determinar o resultado final do
   counter;
2. duracao da persistencia em banco, usada exclusivamente para o histogram
   `notifications.persistence.duration`.

Estrutura sugerida:

1. iniciar variavel local `result = unexpected_error` ao entrar em
   `ProcessMessageAsync(...)`;
2. ao detectar JSON invalido ou falha de validacao, definir `result = invalid_payload`;
3. antes de `SaveChangesAsync(...)`, iniciar timer de persistencia;
4. apos sucesso no banco, registrar histogram com `result = persisted` e definir
   counter final como `persisted`;
5. em falha de banco, registrar histogram com `result = persistence_failed` e
   definir counter final igual;
6. no `ConsumeException` e no error handler do Kafka, registrar `consume_failed`
   no counter apenas uma vez por evento observavel.

## Histogram Boundary

`notifications.persistence.duration` mede apenas o trecho de banco:

1. `dbContext.NotificationResults.Add(...)`
2. `await dbContext.SaveChangesAsync(...)`

Nao inclui desserializacao, validacao ou lag refresh.

---

## Dependency Injection Plan

## OrderService

Registrar em DI:

1. `OrderBacklogSnapshot` como singleton
2. `IOrderMetrics` / `OrderMetrics` como singleton
3. `OrderBacklogSampler` como hosted service

## ProcessingWorker

Registrar em DI:

1. `KafkaLagSnapshot` como singleton
2. `IProcessingMetrics` / `ProcessingMetrics` como singleton
3. `ProcessingLagRefresher` como hosted service ou refresher bounded equivalente

## NotificationWorker

Registrar em DI:

1. `KafkaLagSnapshot` como singleton
2. `INotificationMetrics` / `NotificationMetrics` como singleton
3. `NotificationLagRefresher` como hosted service ou refresher bounded equivalente

Todos os recorders devem ser singleton porque os instrumentos do meter devem ser
criados uma unica vez por processo.

---

## Label and Result Matrix

## OrderService

`orders.created.total` e `orders.create.duration`:

- `created`
- `validation_failed`
- `persist_failed`
- `publish_failed`
- `status_update_failed`

`orders.backlog.current`:

- `status=pending_publish`
- `status=publish_failed`

## ProcessingWorker

`orders.processed.total` e `orders.processing.duration`:

- `processed`
- `invalid_payload`
- `not_found`
- `http_error`
- `timeout`
- `network_error`
- `publish_failed`
- `unexpected_error`

`kafka.consumer.lag`:

- `topic=orders`
- `consumer_group=processing-worker` ou valor configurado

## NotificationWorker

`notifications.persisted.total`:

- `persisted`
- `invalid_payload`
- `persistence_failed`
- `consume_failed`
- `unexpected_error`

`notifications.persistence.duration`:

- `persisted`
- `persistence_failed`

`kafka.consumer.lag`:

- `topic=notifications`
- `consumer_group=notification-worker` ou valor configurado

---

## Failure Handling Design

### Export path failure

Se OTLP, collector ou LGTM falharem, a feature nao deve alterar o comportamento
funcional de requests, consumes, publishes ou persistencia. A observabilidade
degradada fica restrita a perda temporaria de metricas exportadas.

### Sampler failure

Se um sampler de backlog ou lag falhar:

1. registrar log diagnostico sucinto;
2. preservar o ultimo snapshot conhecido ou fallback seguro;
3. nao propagar excecao para derrubar o host.

### Double count protection

O design precisa evitar dupla contagem quando um metodo interno registra erro e o
catch externo tenta registrar novamente o mesmo resultado. A implementacao deve
ter uma unica fronteira de gravacao por mensagem/request.

---

## Validation Design

## Build and startup

1. compilar a solution em container SDK 10;
2. subir `docker compose up -d --build`;
3. confirmar que os tres servicos continuam saudaveis;
4. confirmar que o collector segue com pipeline `metrics` ativa.

## Metric existence checks

Gerar trafego real e consultar no backend:

- nomes canonicos no codigo:
  - `orders.created.total`
  - `orders.create.duration`
  - `orders.backlog.current`
  - `orders.processed.total`
  - `orders.processing.duration`
  - `notifications.persisted.total`
  - `notifications.persistence.duration`
  - `kafka.consumer.lag`

- nomes possivelmente normalizados no Explore/Prometheus:
  - `orders_created_total`
  - `orders_create_duration`
  - `orders_backlog_current`
  - `orders_processed_total`
  - `orders_processing_duration`
  - `notifications_persisted_total`
  - `notifications_persistence_duration`
  - `kafka_consumer_lag`

## Happy path validation

1. criar pedido valido com `POST /orders`;
2. aguardar consumo completo ate persistencia no `NotificationWorker`;
3. validar incremento de counters de sucesso nos tres servicos;
4. validar amostras recentes nos histograms relevantes;
5. validar lag/backlog com valores coerentes e labels baixas em cardinalidade.

## Error path validation

1. `OrderService` com Kafka indisponivel para validar `publish_failed` e backlog;
2. `ProcessingWorker` com `404` ou timeout para validar resultado de erro no counter/histogram;
3. `NotificationWorker` com payload invalido e falha de banco para validar
   `invalid_payload` e `persistence_failed`.

## Debug path

Quando a serie nao aparecer no Explore, a validacao deve seguir esta ordem:

1. conferir logs do servico para saber se o ponto de emissao foi alcancado;
2. conferir logs do `otelcol` com exporter `debug` para ver se o datapoint saiu
   do servico;
3. conferir nome normalizado da metrica no backend;
4. so depois investigar dashboard ou query especifica do Grafana.

---

## Implementation Slices

### Slice 1: Bootstrap de metrics

- estender `OtelExtensions` dos tres servicos com `WithMetrics(...)`
- registrar meters customizados em DI
- garantir build sem mudar fluxo funcional

### Slice 2: OrderService metrics

- adicionar recorder do `OrderService`
- instrumentar `POST /orders`
- adicionar sampler/snapshot de backlog

### Slice 3: ProcessingWorker metrics

- adicionar recorder do worker
- instrumentar resultados e duracao por mensagem
- adicionar snapshot/refresher de lag

### Slice 4: NotificationWorker metrics

- adicionar recorder do worker
- instrumentar counter de resultado e histogram de persistencia
- adicionar snapshot/refresher de lag

### Slice 5: Validacao integrada

- executar caminhos feliz e degradados
- confirmar chegada no backend e ausencia de labels de alta cardinalidade

Esses slices devem ser a base direta para o `tasks.md` da proxima iteracao.

---

## Residual Risks

### Risk 1: Overhead indevido por refresh de lag frequente demais

Se o refresher consultar o broker com intervalo muito curto, pode adicionar ruido
e custo desnecessario ao worker.

**Mitigation**: usar intervalo fixo conservador, timeout curto e agregacao minima.

### Risk 2: Duplicidade de metricas por registrar instrumentos mais de uma vez

Criar instrumentos em classes transitivas ou por request pode produzir erro ou
comportamento inesperado.

**Mitigation**: manter recorders singleton e criacao unica do meter por processo.

### Risk 3: Divergencia entre nome do instrumento e nome consultado no backend

A equipe pode procurar o nome canonico com dots no Explore e nao encontrar a
serie por causa da normalizacao Prometheus.

**Mitigation**: documentar explicitamente as duas formas de nome na validacao.

### Risk 4: Sampler de backlog disputar recursos com a API sob carga

Mesmo agregado, um sampler mal configurado pode competir com requests reais.

**Mitigation**: usar query simples, intervalo moderado e captura de falha sem retry agressivo.
