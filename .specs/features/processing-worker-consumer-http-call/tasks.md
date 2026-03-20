# ProcessingWorker Consumer + HTTP Call — Tasks

**Design**: `.specs/features/processing-worker-consumer-http-call/design.md`
**Status**: Completed

---

## Execution Plan

### Phase 1: Infra de consumo e contratos

```
T1 (dependencias/config) -> T2 (contratos e helper W3C) -> T3 (consumer Kafka)
```

### Phase 2: Enriquecimento HTTP e publish

```
T3 -> T4 (HttpClient + cliente OrderService) -> T5 (pipeline Worker)
T2 -> T6 (publisher notifications)
```

### Phase 3: Observabilidade e validacao

```
T4 + T5 + T6 -> T7 (OTel + logs) -> T8 (smoke tests locais) -> T9 (validacao no Tempo)
```

---

## Task Breakdown

### T1: Adicionar dependencias e configuracoes do ProcessingWorker

**What**: Atualizar o projeto para suportar consumer/producer Kafka, cliente HTTP e configuracoes necessarias ao fluxo.
**Where**: `src/ProcessingWorker/ProcessingWorker.csproj`, `src/ProcessingWorker/appsettings*.json`, `docker-compose.yaml`
**Depends on**: feature `order-service-api-persistencia` concluida

**Done when**:
- [x] `Confluent.Kafka` esta referenciado no `ProcessingWorker`
- [x] Existe configuracao para `ORDER_SERVICE_BASE_URL`
- [x] Existem configuracoes para topics `orders` e `notifications`
- [x] O build do projeto passa em ambiente validado da solution

---

### T2: Introduzir contratos locais e helper W3C reutilizavel no worker

**What**: Criar os contratos minimos de entrada/saida e adaptar o helper de tracing Kafka para extracao e injecao W3C.
**Where**: `src/ProcessingWorker/Contracts/`, `src/ProcessingWorker/Messaging/`
**Depends on**: T1

**Done when**:
- [x] Existe contrato para desserializar a mensagem de `orders`
- [x] Existe contrato minimo para publicar em `notifications`
- [x] O helper expoe `Extract(Headers)` e `Inject(Activity?, Headers)`
- [x] Headers `traceparent` e `tracestate` sao lidos e escritos sem logica inline no worker

---

### T3: Implementar o consumer Kafka do topic `orders`

**What**: Registrar e configurar o consumer com group id dedicado, assinando `orders` e entregando mensagens validas ao pipeline de processamento.
**Where**: `src/ProcessingWorker/Program.cs`, `src/ProcessingWorker/Worker.cs` ou componentes de messaging associados
**Depends on**: T2

**Done when**:
- [x] O worker assina o topic `orders`
- [x] Mensagens sao desserializadas em um contrato coerente com `OrderCreatedEvent`
- [x] Payload invalido ou sem `orderId` gera log estruturado e nao derruba o host
- [x] O loop respeita `CancellationToken` e continua saudavel apos erros de consumo/desserializacao

---

### T4: Registrar `HttpClient` instrumentado e cliente do OrderService

**What**: Criar um cliente HTTP dedicado para `GET /orders/{id}` com timeout configuravel e tratamento explicito de `200`, `404`, `5xx`, timeout e falha de rede.
**Where**: `src/ProcessingWorker/Program.cs`, `src/ProcessingWorker/Clients/`
**Depends on**: T1

**Done when**:
- [x] Existe `HttpClient` registrado em DI com `BaseAddress` vindo de configuracao
- [x] O timeout e configuravel sem recompilar o projeto
- [x] `404` e distinguido de falhas tecnicas
- [x] O cliente devolve dados coerentes com `OrderResponse` ou um resultado de falha claro para o worker

---

### T5: Implementar o pipeline de processamento Kafka -> HTTP

**What**: Orquestrar no `Worker` a extracao de contexto, criacao do span de consumo, chamada HTTP e validacoes de consistencia antes da publicacao.
**Where**: `src/ProcessingWorker/Worker.cs`
**Depends on**: T3, T4

**Done when**:
- [x] O span manual `kafka consume orders` e criado com `ActivityKind.Consumer`
- [x] O `GET /orders/{id}` ocorre dentro do contexto do span de consumo
- [x] `404`, `5xx`, timeout e falha de rede nao publicam em `notifications`
- [x] Divergencia entre `orderId` consumido e retornado e tratada como erro observavel

---

### T6: Implementar o publisher Kafka para `notifications`

**What**: Encapsular a montagem e a publicacao do evento enriquecido no topic `notifications` com propagacao W3C.
**Where**: `src/ProcessingWorker/Messaging/`
**Depends on**: T2, T5

**Done when**:
- [x] Existe publisher dedicado para `notifications`
- [x] O payload publicado contem `orderId`, `description`, `status`, `createdAtUtc`, `publishedAtUtc` e `processedAtUtc`
- [x] `traceparent` e `tracestate` sao propagados manualmente nos headers
- [x] Falhas de publish geram erro observavel sem encerrar o host

---

### T7: Expandir observabilidade do ProcessingWorker

**What**: Ajustar bootstrap OTel, `ActivitySource` e logs para refletir claramente o fluxo Kafka consumer -> HTTP client -> Kafka producer.
**Where**: `src/ProcessingWorker/Extensions/OtelExtensions.cs`, `src/ProcessingWorker/Worker.cs`, `src/ProcessingWorker/Messaging/`
**Depends on**: T5, T6

**Done when**:
- [x] O `ActivitySource` do worker inclui spans de consumo e publish
- [x] O `HttpClient` gera spans automaticamente dentro do mesmo trace
- [x] Logs relevantes incluem `orderId`, `TraceId` e `SpanId`
- [x] Erros de negocio e tecnicos ficam distinguiveis em trace e log

---

### T8: Executar smoke tests locais do fluxo feliz e dos cenarios de erro

**What**: Subir o ambiente, gerar pedidos reais e validar comportamento visivel em Kafka, HTTP e logs do worker.
**Where**: execucao local via Docker Compose e ferramentas ja usadas no projeto
**Depends on**: T7

**Done when**:
- [x] `docker compose up -d --build order-service processing-worker kafka postgres otelcol` conclui sem novo erro funcional
- [x] Um `POST /orders` saudavel gera mensagem em `notifications` com payload enriquecido e headers W3C
- [x] Um `404` nao gera mensagem em `notifications`
- [x] Uma falha tecnica HTTP nao derruba o `processing-worker` nem gera mensagem em `notifications`

---

### T9: Validar o trace distribuido no Tempo

**What**: Confirmar no Tempo que o fluxo do `ProcessingWorker` aparece no mesmo `TraceId` do pedido criado no `OrderService`.
**Where**: Grafana Tempo e inspeção dos artefatos locais
**Depends on**: T8

**Done when**:
- [x] O caminho feliz mostra `POST /orders` -> `kafka publish orders` -> `kafka consume orders` -> `GET /orders/{id}` -> `kafka publish notifications`
- [x] O `TraceId` do `processing-worker` coincide com o iniciado no `order-service`
- [x] O caminho de `404` mostra span HTTP com `404` e ausencia de `kafka publish notifications`
- [x] O caminho de falha tecnica HTTP mostra erro observavel sem span de producer subsequente

---

## Validation Notes

- Build do projeto e da solution validados com SDK 10 em container Docker.
- Fluxo feliz validado com `POST /orders`, inspeção do topic `notifications` e consulta direta ao Tempo.
- Caminhos de `404`, timeout e `5xx` validados sem publish em `notifications`.
- Mensagem sem headers W3C validada com warning estruturado e inicio de novo trace no `ProcessingWorker`.

---

## Parallel Execution Map

```
Phase 1:
  T1 -> T2 -> T3

Phase 2:
  T1 -> T4
  T3 + T4 -> T5
  T2 + T5 -> T6

Phase 3:
  T5 + T6 -> T7 -> T8 -> T9
```