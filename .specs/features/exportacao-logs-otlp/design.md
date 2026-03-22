# Exportação de Logs OTLP — Design

**Spec**: `.specs/features/exportacao-logs-otlp/spec.md`
**Status**: Draft

---

## Architecture Overview

O OTel Collector já possui pipeline de logs configurado e funcional. A única lacuna está no lado dos serviços .NET: nenhum deles registra um `OpenTelemetryLoggerProvider` no ILoggingBuilder do host.

O ajuste é cirúrgico: cada `OtelExtensions.cs` adiciona uma chamada a `builder.Logging.AddOpenTelemetry(...)` usando o mesmo endpoint OTLP gRPC já configurado para traces e métricas. O pipeline do collector e o backend Loki não sofrem nenhuma alteração.

O fix do `ProcessingWorker` é igualmente cirúrgico: `HandleLookupOutcome` passa a rejeitar pedidos cujo status não seja `published`, antes de qualquer acesso a `PublishedAtUtc`.

```mermaid
flowchart LR
    subgraph Serviços .NET
        A["ILogger<T>\n(já existente)"]
        B["OpenTelemetryLoggerProvider\n(novo)"]
        A --> B
    end

    B -->|OTLP gRPC| C[OTel Collector\notelcol]

    subgraph Collector — sem alteração
        C --> D["pipeline logs\n(já configurado)"]
        D --> E["otlphttp/logs\n(já configurado)"]
    end

    E -->|OTLP HTTP| F[LGTM — Loki]
    F --> G[Grafana Explore]
```

---

## Code Reuse Analysis

### Existing Components to Leverage

| Componente | Localização | Como reutilizar |
| --- | --- | --- |
| `OtelExtensions.AddOtelInstrumentation` | `src/*/Extensions/OtelExtensions.cs` | Adicionar `builder.Logging.AddOpenTelemetry(...)` no mesmo método, depois de `AddOpenTelemetry()` |
| `otlpEndpoint` | `OtelExtensions.cs` (leitura de `OTEL_EXPORTER_OTLP_ENDPOINT`) | Reutilizar a mesma variável sem duplicação |
| `resourceBuilder` | `OtelExtensions.cs` | Reutilizar o `ResourceBuilder` já criado para manter `service.name` e `service.version` consistentes |
| `IServiceCollection.AddOtelInstrumentation` | `Program.cs` de cada serviço | Nenhuma mudança necessária nos `Program.cs` |

### Integration Points

| Sistema | Método de integração |
| --- | --- |
| `ILoggingBuilder` do .NET Host | `builder.Logging.AddOpenTelemetry(...)` — registra o provider OTel antes do host construir |
| OTel Collector pipeline `logs` | Já aceita OTLP gRPC na porta 4317 — nenhuma mudança necessária |
| Loki via `otlphttp/logs` | Já configurado no collector — nenhuma mudança necessária |

---

## Components

### `OtelExtensions` — OrderService

- **Purpose**: Adicionar provider de logs OTel ao ILoggingBuilder do OrderService
- **Location**: `src/OrderService/Extensions/OtelExtensions.cs`
- **Interfaces**:
  - `AddOtelInstrumentation(IServiceCollection, IConfiguration): IServiceCollection` — assinatura pública mantida, sem alteração de contrato
- **Dependencies**: `OpenTelemetry.Extensions.Hosting` (já presente), `OpenTelemetry.Exporter.OpenTelemetryProtocol` (já presente)
- **Reuses**: `otlpEndpoint`, `resourceBuilder` já criados no método

**Mudança necessária**: `AddOtelInstrumentation` recebe `IServiceCollection services` mas precisa de acesso ao `ILoggingBuilder`. A forma correta em .NET é chamar `services.AddLogging(logging => logging.AddOpenTelemetry(...))`. Essa API está disponível via `OpenTelemetry.Extensions.Hosting` e não requer novos pacotes.

### `OtelExtensions` — ProcessingWorker

- **Purpose**: Adicionar provider de logs OTel ao ILoggingBuilder do ProcessingWorker
- **Location**: `src/ProcessingWorker/Extensions/OtelExtensions.cs`
- **Interfaces**: mesma assinatura pública inalterada
- **Dependencies**: pacotes já presentes
- **Reuses**: `otlpEndpoint`, `resourceBuilder` já criados no método

### `OtelExtensions` — NotificationWorker

- **Purpose**: Adicionar provider de logs OTel ao ILoggingBuilder do NotificationWorker
- **Location**: `src/NotificationWorker/Extensions/OtelExtensions.cs`
- **Interfaces**: mesma assinatura pública inalterada
- **Dependencies**: pacotes já presentes
- **Reuses**: `otlpEndpoint`, `resourceBuilder` já criados no método

### `Worker.HandleLookupOutcome` — ProcessingWorker (fix)

- **Purpose**: Corrigir a guarda que valida `PublishedAtUtc` para cobrir todos os status diferentes de `published`, evitando `NullReferenceException` na camada de construção do `NotificationRequestedEvent`
- **Location**: `src/ProcessingWorker/Worker.cs`
- **Interfaces**: método privado `HandleLookupOutcome` — assinatura inalterada
- **Reuses**: padrão já existente de `result = ProcessingResults.InvalidPayload` + log + `return false`

---

## Configuração do Logger Provider

Cada `OtelExtensions.cs` deve adicionar, dentro de `AddOtelInstrumentation`, após o bloco `AddOpenTelemetry()`:

```csharp
services.AddLogging(logging =>
    logging.AddOpenTelemetry(options =>
    {
        options.SetResourceBuilder(resourceBuilder);
        options.AddOtlpExporter(exporter =>
        {
            exporter.Endpoint = new Uri(otlpEndpoint);
            exporter.Protocol = OtlpExportProtocol.Grpc;
        });
        options.IncludeFormattedMessage = true;
        options.IncludeScopes = true;
    }));
```

> **Nota sobre console logging**: `AddOpenTelemetry()` em logs é aditivo — não remove o provider de console padrão. Os logs continuam visíveis no `docker compose logs` e também são exportados para o Loki.

---

## Fix: `HandleLookupOutcome` no ProcessingWorker

### Comportamento atual (bugado)

```
order status = "pending_publish", PublishedAtUtc = null
   └→ HandleLookupOutcome: valida apenas (status == "published" && PublishedAtUtc == null) → não entra no if
   └→ retorna true
   └→ ProcessMessageAsync: acessa order.PublishedAtUtc!.Value  ← EXPLODE
```

### Comportamento esperado após fix

```
order status qualquer != "published"
   └→ HandleLookupOutcome: verifica se status é != "published"
   └→ registra log com Classification=order_not_published, marca span com erro
   └→ retorna false
   └→ ProcessMessageAsync: não constrói NotificationRequestedEvent, encerra sem exceção
```

A validação adicional deve ser inserida **antes** do bloco que valida `publishedAtUtc is null`, aproveitando o mesmo padrão `result = InvalidPayload + activity + log + return false` já usado para outros casos de erro.

---

## Error Handling Strategy

| Cenário | Tratamento | Impacto visível |
| --- | --- | --- |
| OTel Collector indisponível no startup | O host sobe normalmente; logs não são exportados até o collector reconectar. Comportamento gerenciado pelo SDK. | Logs visíveis apenas no stdout |
| Log emitido fora de span ativo | `TraceId` fica vazio no Loki — comportamento esperado | Nenhum impacto — correlação só existe quando há span |
| Pedido com status diferente de `published` no ProcessingWorker | Log estruturado + span com erro + retorno sem publicação | `fail: ProcessingWorker.Worker` substituído por log explicativo |

---

## Tech Decisions

| Decisão | Escolha | Rationale |
| --- | --- | --- |
| API de registro de logs | `services.AddLogging(logging => logging.AddOpenTelemetry(...))` em vez de `builder.Logging.AddOpenTelemetry(...)` diretamente | `AddOtelInstrumentation` recebe `IServiceCollection`, não `IHostApplicationBuilder`. A API `AddLogging` é compatível e disponível nos pacotes já referenciados. |
| Protocolo do exporter de logs | OTLP gRPC (mesmo protocolo de traces e métricas) | Consistência de configuração; a porta 4317 já está mapeada e usada pelos outros sinais |
| Console logging | Mantido (aditivo) | Não remove a observabilidade local via `docker compose logs`, que é o caminho principal do README |
| IncludeFormattedMessage | `true` | Sem isso o Loki recebe apenas o template do log (`"Order created {OrderId}"`) e não a mensagem preenchida. Necessário para diagnóstico |
| IncludeScopes | `true` | Permite capturar o escopo do request HTTP no `order-service` e scopes de DI nos workers |
