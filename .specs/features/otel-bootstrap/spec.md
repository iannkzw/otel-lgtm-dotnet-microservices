# OpenTelemetry Bootstrap — Specification

**Milestone**: M1 — Infraestrutura e Esqueleto dos Serviços
**Status**: Planned

---

## Problem Statement

Os 3 serviços .NET da PoC existem como esqueleto mas ainda não enviam nenhuma telemetria. Para que M1 seja considerado concluído, precisamos que cada serviço apareça como source distinto no Grafana Tempo, provando que a pipeline OTel — de serviço até o backend LGTM — funciona de ponta a ponta.

## Goals

- [ ] OpenTelemetry configurado nos 3 serviços com OTLP exporter apontando para `otelcol:4317`
- [ ] Resource configurado com `service.name` e `service.version` em cada serviço
- [ ] `OrderService` instrumentado com `AspNetCore` (traces de request HTTP)
- [ ] Os 3 serviços instrumentados com `Http` (traces de chamadas HTTP outbound)
- [ ] 3 serviços visíveis como sources distintos no Grafana Tempo

## Out of Scope

- Instrumentação de Kafka (`Confluent.Kafka`) — será feita em M2 junto com a lógica de consumer/producer
- Instrumentação de Entity Framework Core — será feita em M2 junto com a implementação do banco
- Métricas customizadas — serão adicionadas em M3
- Logs estruturados com correlação de trace — serão adicionados em M2

---

## User Stories

### P1: OTLP exporter configurado via variáveis de ambiente ⭐ MVP

**User Story**: Como engenheiro de plataforma, quero que os serviços .NET enviem traces para o OTel Collector via OTLP gRPC, usando as variáveis de ambiente já definidas no Compose, para que a telemetria flua sem configuração hardcoded.

**Why P1**: É o mecanismo central de toda a observabilidade da PoC.

**Acceptance Criteria**:

1. WHEN `OTEL_EXPORTER_OTLP_ENDPOINT` está definido como `http://otelcol:4317` THEN o exporter SHALL enviar dados para esse endpoint sem configuração adicional no código
2. WHEN o OTel Collector não está acessível THEN o serviço SHALL continuar funcionando (sem crash) e logar o erro de exportação
3. WHEN o exporter é configurado THEN ele SHALL usar protocolo gRPC (não HTTP/protobuf)

**Independent Test**: Iniciar apenas `OrderService` com `OTEL_EXPORTER_OTLP_ENDPOINT` apontando para o collector real e verificar spans no Tempo.

---

### P1: Resource com service.name e service.version ⭐ MVP

**User Story**: Como engenheiro de observabilidade, quero que cada serviço se identifique com `service.name` e `service.version` no Resource OTel, para que os serviços apareçam como sources distintos e identificáveis no Tempo.

**Why P1**: Sem `service.name` os traces chegam sem identificação e são impossíveis de filtrar.

**Acceptance Criteria**:

1. WHEN `OrderService` envia traces THEN o Resource SHALL ter `service.name=order-service`
2. WHEN `ProcessingWorker` envia traces THEN o Resource SHALL ter `service.name=processing-worker`
3. WHEN `NotificationWorker` envia traces THEN o Resource SHALL ter `service.name=notification-worker`
4. WHEN qualquer serviço envia traces THEN o Resource SHALL ter `service.version` com o valor da versão do assembly
5. WHEN os 3 serviços estão rodando THEN o Grafana Tempo SHALL exibir 3 sources distintos filtráveis por `service.name`

**Independent Test**: Abrir o Grafana Tempo, aplicar filtro `service.name = order-service` e verificar que spans aparecem.

---

### P1: Instrumentação AspNetCore no OrderService ⭐ MVP

**User Story**: Como engenheiro de observabilidade, quero que toda request HTTP recebida pelo `OrderService` gere automaticamente um span no trace, para que qualquer chamada à API seja rastreável sem código manual.

**Why P1**: O `OrderService` é o ponto de entrada principal da PoC — sem isso não há trace root.

**Acceptance Criteria**:

1. WHEN uma request HTTP chega ao `OrderService` THEN um span SHALL ser criado automaticamente com `http.method`, `http.route` e `http.status_code`
2. WHEN a request retorna 2xx THEN o span SHALL ter status `OK`
3. WHEN a request retorna 5xx THEN o span SHALL ter status `ERROR`
4. WHEN health check endpoint é chamado THEN ele SHALL ser filtrado pelo processor `drop-health-checks` existente e NÃO SHALL aparecer no Tempo

**Independent Test**: Fazer uma chamada `GET /healthz` (ou similar) e verificar que não aparece no Tempo; fazer chamada a outro endpoint e verificar que aparece.

---

### P1: Instrumentação Http (outbound) nos 3 serviços ⭐ MVP

**User Story**: Como engenheiro de observabilidade, quero que chamadas HTTP de saída feitas pelos serviços sejam automaticamente rastreadas, para que o trace distribuído inclua spans de chamadas HTTP entre serviços.

**Why P1**: Em M2 o `ProcessingWorker` fará um HTTP GET para o `OrderService` — a instrumentação precisa estar pronta.

**Acceptance Criteria**:

1. WHEN qualquer serviço faz uma chamada HTTP outbound via `HttpClient` THEN um span filho SHALL ser criado automaticamente
2. WHEN o span de saída é criado THEN ele SHALL propagar o trace context via headers W3C TraceContext (`traceparent`, `tracestate`)

**Independent Test**: Em M1, validar que `AddHttpClientInstrumentation()` está registrado nos 3 serviços e que a verificação funcional dos spans outbound dos workers fica coberta quando houver chamadas HTTP reais em M2.

---

### P2: Configuração OTel em método de extensão reutilizável

**User Story**: Como desenvolvedor, quero que a configuração OTel seja feita em um método de extensão (ex: `AddOtelInstrumentation()`) em cada projeto, para que o `Program.cs` fique limpo e a configuração seja encapsulada.

**Why P2**: Melhora legibilidade e facilita expansão em M2/M3, mas `Program.cs` verboso ainda funciona.

**Acceptance Criteria**:

1. WHEN `Program.cs` é lido THEN a configuração OTel SHALL estar em no máximo 1-2 linhas via método de extensão
2. WHEN um novo tipo de instrumentação é adicionado THEN a mudança SHALL estar isolada no método de extensão, não no `Program.cs`

**Independent Test**: Contar linhas de configuração OTel no `Program.cs` — deve ser ≤ 2.

---

## Edge Cases

- WHEN `OTEL_EXPORTER_OTLP_ENDPOINT` não está definido THEN o serviço SHALL usar `localhost:4317` como fallback e logar um aviso
- WHEN o OTel Collector está down no momento do startup THEN o serviço SHALL iniciar normalmente e tentar reenviar em background
- WHEN `service.version` do assembly não está definido THEN o Resource SHALL usar `"0.0.0"` como fallback

---

## Success Criteria

- [ ] 3 serviços visíveis como sources distintos no Grafana Tempo (`service.name` correto)
- [ ] Spans de request HTTP aparecem no `OrderService` para qualquer endpoint chamado
- [ ] Health checks não aparecem no Tempo (filtrados pelo processor existente)
- [ ] Nenhum serviço crasha quando o OTel Collector está inacessível
