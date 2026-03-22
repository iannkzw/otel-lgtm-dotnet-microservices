# Exportação de Logs OTLP Specification

## Problem Statement

Os três serviços .NET emitem logs via `ILogger<T>` do ASP.NET/Extensions, mas nenhum deles registra um provider de logs OpenTelemetry. O `OtelExtensions` de cada serviço configura somente `.WithTracing(...)` e `.WithMetrics(...)`, sem nenhuma chamada a `builder.Logging.AddOpenTelemetry(...)`. Por isso os logs ficam apenas no stdout do container e nunca chegam ao OTel Collector — que já tem o pipeline de logs configurado e funcional —, resultando no Loki completamente vazio para esses serviços.

Existe também um bug ativo no `ProcessingWorker`: quando um pedido retorna da API com status `pending_publish`, o `HandleLookupOutcome` aprova o processamento (retorna `true`), mas a linha seguinte acessa `order.PublishedAtUtc!.Value` sem guarda, causando `InvalidOperationException: Nullable object must have a value` e propagando o erro como falha inesperada do pipeline.

## Goals

- [ ] Os três serviços exportam logs via OTLP gRPC para o OTel Collector, que os encaminha para o Loki
- [ ] Os logs exportados contêm `TraceId` e `SpanId` no corpo do registro, permitindo correlação com traces no Tempo
- [ ] O Loki recebe registros visíveis no Grafana com o label `service_name` identificando cada serviço
- [ ] O `ProcessingWorker` não explode com `NullReferenceException` ao processar pedidos em status diferente de `published`

## Out of Scope

- Troca do provider de log padrão do .NET (o console logging permanece ativo)
- Adição de campos estruturados novos além dos que já existem nos `LogInformation`/`LogError` existentes
- Ingestão de logs de containers por Promtail ou qualquer mecanismo de scraping
- Alterações no `otelcol.yaml`, nos pipelines do collector ou no backend Loki/Grafana
- Reescrita de chamadas de log já existentes nos serviços

---

## User Stories

### P1: Logs dos serviços chegam ao Loki ⭐ MVP

**User Story**: Como engenheiro de plataforma, quero ver logs dos serviços .NET no Loki para correlacionar eventos de negócio com traces no Tempo.

**Why P1**: O objetivo G3 do projeto afirma "logs estruturados correlacionados ao trace (TraceId/SpanId nos logs) — verificável via query no Loki". Hoje isso é impossível porque nenhum log chega ao Loki.

**Acceptance Criteria**:

1. WHEN um pedido é criado via `POST /orders` THEN o Loki SHALL receber ao menos um registro com `service_name = "order-service"` dentro de 30 segundos
2. WHEN o `processing-worker` consome uma mensagem do topic `orders` THEN o Loki SHALL receber ao menos um registro com `service_name = "processing-worker"`
3. WHEN o `notification-worker` persiste uma notificação THEN o Loki SHALL receber ao menos um registro com `service_name = "notification-worker"`
4. WHEN qualquer log é exportado THEN o campo `TraceId` SHALL estar presente e não vazio no body do registro no Loki

**Independent Test**: Executar `POST /orders`, aguardar 30s e consultar `{service_name="order-service"}` no Grafana Explore/Loki. Deve retornar resultados.

---

### P1: Fix — ProcessingWorker não estoura com `pending_publish` ⭐ MVP

**User Story**: Como desenvolvedor, quero que o `ProcessingWorker` lide corretamente com pedidos em status diferente de `published`, sem gerar exceções inesperadas que encobrem a causa raiz nos logs e traces.

**Why P1**: O bug gera falhas silenciosas no pipeline (`Unexpected processing failure`) sem telemetria precisa. Além disso, encobre o diagnóstico de logs uma vez que os registros de erro aparecem sem contexto de observabilidade correto.

**Acceptance Criteria**:

1. WHEN o `ProcessingWorker` recebe um pedido com status `pending_publish` (sem `PublishedAtUtc`) THEN o worker SHALL registrar o motivo como `order_not_published` e retornar sem publicar em `notifications`, sem exceção
2. WHEN o `ProcessingWorker` recebe um pedido com status `published` e `PublishedAtUtc` preenchido THEN o fluxo feliz SHALL continuar inalterado
3. WHEN ocorre o tratamento do status inválido THEN o span SHALL ser marcado com erro e o log SHALL conter `OrderId`, `TraceId` e `SpanId`

**Independent Test**: Criar um pedido e, antes do processamento, travar o Kafka por alguns segundos (para que o status fique `pending_publish`). Confirmar que nenhum `InvalidOperationException` aparece nos logs do worker.

---

## Edge Cases

- WHEN o OTel Collector estiver indisponível THEN os serviços SHALL continuar funcionando normalmente e os logs locais SHALL permanecer visíveis via `docker compose logs`
- WHEN um log é emitido fora de um span ativo (ex: startup) THEN o campo TraceId SHALL estar ausente ou vazio — isso é esperado e não é erro
- WHEN IncludeFormattedMessage e IncludeScopes estiverem habilitados THEN os logs exportados SHALL incluir a mensagem formatada completa, mantendo compatibilidade com o formato atual dos logs estruturados existentes

---

## Success Criteria

- [ ] Consulta `{service_name="order-service"}` no Loki retorna resultados após `POST /orders`
- [ ] Consulta `{service_name="processing-worker"}` e `{service_name="notification-worker"}` retornam resultados após fluxo completo
- [ ] Ao menos um log visível no Loki possui o campo `TraceId` não vazio
- [ ] Build da solution em container SDK 10 passa sem erros
- [ ] Nenhum `InvalidOperationException: Nullable object must have a value` nos logs do `processing-worker` após execução de fluxo feliz
- [ ] Logs do `otelcol` no modo debug exibem eventos do tipo `Logs` vindos dos serviços .NET (confirma chegada ao collector)
