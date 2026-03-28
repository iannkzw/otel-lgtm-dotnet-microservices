# 04 - Logging Estruturado

## Indice

1. [O que sao Logs Estruturados](#1-o-que-sao-logs-estruturados)
2. [Niveis de Log](#2-niveis-de-log)
3. [Correlacao Logs-Traces](#3-correlacao-logs-traces)
4. [Como a PoC exporta logs](#4-como-a-poc-exporta-logs)
5. [LogQL essencial (Loki)](#5-logql-essencial-loki)
6. [Boas praticas](#6-boas-praticas)
7. [Referencias](#7-referencias)

---

## 1. O que sao Logs Estruturados

Logs estruturados sao registros de eventos emitidos pela aplicacao em um formato padronizado e parseavel por maquina -- tipicamente JSON. A diferenca fundamental em relacao a logs de texto livre e a capacidade de consulta, filtragem e agregacao automatizada.

### Texto livre vs JSON

**Texto livre (nao estruturado):**

```
2026-03-25 10:32:15 INFO Pedido 12345 criado com sucesso para o cliente C-99
```

**JSON (estruturado):**

```json
{
  "timestamp": "2026-03-25T10:32:15.123Z",
  "level": "Information",
  "message": "Pedido {OrderId} criado com sucesso para o cliente {CustomerId}",
  "OrderId": 12345,
  "CustomerId": "C-99",
  "service.name": "order-service",
  "traceId": "abc123def456...",
  "spanId": "789ghi..."
}
```

### Por que estruturar?

| Aspecto | Texto livre | Estruturado |
|---------|-------------|-------------|
| Busca por campo | Regex fragil | Filtro exato por chave |
| Agregacao | Manual | Automatica (count, group by) |
| Correlacao | Dificil | TraceId/SpanId nativos |
| Consumo por ferramentas | Parsing customizado | Suporte nativo (Loki, Elasticsearch) |
| Evolucao do formato | Quebra parsers | Campos adicionais sem quebra |

---

## 2. Niveis de Log

O .NET define seis niveis de log, do menos ao mais severo. Cada nivel tem um proposito especifico e impacto diferente em producao.

### Tabela de niveis

| Nivel | Valor | Quando usar | Exemplo na PoC |
|-------|-------|-------------|-----------------|
| **Trace** | 0 | Detalhes internos do framework; debug profundo | Detalhes de serializacao de mensagem Kafka |
| **Debug** | 1 | Informacao util para desenvolvimento | `Deserializando mensagem do topico {Topic}` |
| **Information** | 2 | Fluxo normal da aplicacao; eventos de negocio | `Pedido {OrderId} criado com sucesso` |
| **Warning** | 3 | Situacao inesperada mas recuperavel | `Retry #{Attempt} para enriquecimento do pedido {OrderId}` |
| **Error** | 4 | Falha que impede uma operacao especifica | `Falha ao persistir notificacao: {ErrorMessage}` |
| **Critical** | 5 | Falha catastrofica; aplicacao pode parar | `Conexao com banco de dados perdida apos {MaxRetries} tentativas` |

### Impacto em producao

| Nivel minimo configurado | Volume estimado | Custo de armazenamento | Recomendacao |
|--------------------------|-----------------|----------------------|--------------|
| Trace | Muito alto | Alto | Nunca em producao |
| Debug | Alto | Alto | Apenas troubleshooting temporario |
| **Information** | **Moderado** | **Moderado** | **Padrao recomendado** |
| Warning | Baixo | Baixo | Ambientes com restricao de custo |

> **Dica:** Na PoC, o nivel padrao e `Information`. Em producao, considere usar `Warning` como base e habilitar `Information` ou `Debug` sob demanda via configuracao dinamica.

---

## 3. Correlacao Logs-Traces

A correlacao entre logs e traces e um dos maiores beneficios da observabilidade moderna. Ela permite navegar de um log de erro diretamente para o trace distribuido completo que causou o problema.

### TraceId e SpanId nos logs

Cada log emitido dentro de um contexto de trace carrega automaticamente:

- **TraceId** (32 caracteres hex): identifica a transacao distribuida de ponta a ponta
- **SpanId** (16 caracteres hex): identifica a operacao especifica dentro do trace

### Como o .NET injeta automaticamente

Quando `AddOpenTelemetry()` e adicionado ao logging builder, o SDK do OpenTelemetry para .NET:

1. Intercepta cada log emitido via `ILogger`
2. Verifica se existe um `Activity` (span) ativo no contexto atual
3. Extrai `TraceId` e `SpanId` do `Activity.Current`
4. Adiciona esses campos como atributos do log record
5. Exporta via OTLP junto com os demais atributos

Isso acontece **sem nenhuma alteracao no codigo de logging**. O seguinte codigo:

```csharp
_logger.LogInformation("Pedido {OrderId} criado", order.Id);
```

Produz um log com `TraceId` e `SpanId` automaticamente, desde que esteja dentro de uma requisicao HTTP (ASP.NET Core) ou de um span manual.

### Como buscar logs de um trace especifico no Loki

No Grafana, ha duas formas de navegar:

**1. Via Tempo (trace) para Loki (logs):**

- Abra um trace no Tempo
- Clique em "Logs for this span" (correlacao automatica pelo `traceId`)

**2. Via LogQL direto:**

```logql
{service_name="order-service"} | json | traceId = "abc123def456..."
```

**3. Via Grafana Explore:**

- Selecione datasource Loki
- Cole o TraceId no filtro
- Visualize todos os logs de todos os servicos para aquele trace

---

## 4. Como a PoC exporta logs

Cada servico da PoC configura a exportacao de logs no `OtelExtensions.cs` seguindo o mesmo padrao.

### Configuracao (comum aos 3 servicos)

```csharp
services.AddLogging(logging =>
    logging.AddOpenTelemetry(options =>
    {
        options.SetResourceBuilder(resourceBuilder);
        options.AddOtlpExporter(exporter =>
        {
            exporter.Endpoint = new Uri(otlpEndpoint);   // otelcol:4317
            exporter.Protocol = OtlpExportProtocol.Grpc;
        });
        options.IncludeFormattedMessage = true;
        options.IncludeScopes = true;
    }));
```

### Detalhamento das opcoes

| Opcao | Valor | Efeito |
|-------|-------|--------|
| `SetResourceBuilder` | `service.name`, `service.version` | Identifica o servico de origem no Loki |
| `AddOtlpExporter` | gRPC para `otelcol:4317` | Envia logs via OTLP para o Collector |
| `IncludeFormattedMessage` | `true` | Inclui a mensagem ja formatada (com valores substituidos) |
| `IncludeScopes` | `true` | Inclui scopes do logging (ex: `RequestId`, `ConnectionId`) |

### Fluxo completo

```
App (.NET) --> ILogger --> OpenTelemetry Log Bridge --> OTLP gRPC --> OTel Collector --> OTLP HTTP --> Loki
```

### Resource attributes

O `ResourceBuilder.CreateDefault().AddService(serviceName, serviceVersion: serviceVersion)` adiciona:

- `service.name`: ex. `order-service`, `processing-worker`, `notification-worker`
- `service.version`: versao do assembly
- `telemetry.sdk.name`: `opentelemetry`
- `telemetry.sdk.language`: `dotnet`

Esses atributos tornam-se **labels** no Loki, usados para filtrar logs por servico.

---

## 5. LogQL essencial (Loki)

LogQL e a linguagem de consulta do Loki, inspirada em PromQL. Existem dois tipos de consultas: **log queries** (retornam linhas de log) e **metric queries** (retornam series temporais derivadas de logs).

### 5.1 Seletores de stream

O seletor de stream filtra logs por labels (baixo custo, usa indice):

```logql
# Logs do OrderService
{service_name="order-service"}

# Logs do ProcessingWorker com nivel error
{service_name="processing-worker", level="Error"}

# Logs de qualquer worker
{service_name=~".*-worker"}
```

### 5.2 Filtros de linha

Filtros aplicados ao conteudo da linha de log (pos-indice):

```logql
# Contem "error" (case sensitive)
{service_name="order-service"} |= "error"

# NAO contem "health" (remover noise)
{service_name="order-service"} != "health"

# Regex: pedidos com ID numerico
{service_name="order-service"} |~ "OrderId.*[0-9]+"

# Combinando filtros
{service_name="order-service"} |= "Pedido" != "health" != "swagger"
```

### 5.3 Parser JSON e formatacao

```logql
# Parsear campos JSON
{service_name="order-service"} | json

# Filtrar por campo parseado
{service_name="order-service"} | json | OrderId > 100

# Formatar saida
{service_name="order-service"} | json | line_format "{{.timestamp}} [{{.level}}] {{.message}}"
```

### 5.4 Metricas sobre logs

```logql
# Taxa de logs por segundo nos ultimos 5 minutos
rate({service_name="order-service"}[5m])

# Contagem de erros nos ultimos 15 minutos
count_over_time({service_name="order-service", level="Error"}[15m])

# Taxa de erros por servico
sum by (service_name) (rate({level="Error"}[5m]))

# Bytes de log por servico por segundo
bytes_rate({service_name=~".+"}[5m])
```

### 5.5 Exemplos praticos com a PoC

```logql
# Todos os logs de um pedido especifico em todos os servicos
{service_name=~"order-service|processing-worker|notification-worker"} |= "OrderId" | json | OrderId = "12345"

# Erros no ProcessingWorker nas ultimas 2 horas
{service_name="processing-worker", level="Error"}

# Logs de um trace especifico (correlacao)
{service_name=~".+"} | json | traceId = "abc123..."

# Taxa de notificacoes processadas por minuto
rate({service_name="notification-worker"} |= "processada" [1m])

# Top 5 mensagens de erro mais frequentes
topk(5, sum by (message) (count_over_time({level="Error"} | json [1h])))
```

---

## 6. Boas praticas

### 6.1 Use structured logging templates

```csharp
// CORRETO: template com placeholders
_logger.LogInformation("Pedido {OrderId} criado para cliente {CustomerId}", orderId, customerId);

// ERRADO: concatenacao de strings (perde estrutura)
_logger.LogInformation($"Pedido {orderId} criado para cliente {customerId}");

// ERRADO: interpolacao tambem perde a capacidade de agrupar por template
_logger.LogInformation("Pedido " + orderId + " criado para cliente " + customerId);
```

A diferenca: com templates, o Loki (e qualquer backend) consegue agrupar logs pelo **template** e extrair os **valores** como campos separados. Com concatenacao, cada mensagem e unica.

### 6.2 Inclua contexto relevante

```csharp
// Adicione campos que ajudam no troubleshooting
using (_logger.BeginScope(new Dictionary<string, object>
{
    ["CorrelationId"] = correlationId,
    ["UserId"] = userId
}))
{
    _logger.LogInformation("Processando pedido {OrderId}", orderId);
}
```

Campos uteis para incluir:

- **CorrelationId**: identificador da transacao de negocio
- **UserId**: quem iniciou a acao
- **OrderId**, **PaymentId**, etc: entidades de negocio
- **Duration**: tempo de operacoes lentas

### 6.3 O que NAO logar

| Categoria | Exemplo | Risco |
|-----------|---------|-------|
| **PII** (dados pessoais) | CPF, email, nome completo | LGPD, vazamento de dados |
| **Secrets** | Tokens, senhas, API keys | Comprometimento de seguranca |
| **Payloads grandes** | Body de requisicao completo | Custo de armazenamento, performance |
| **Dados de cartao** | Numero, CVV | PCI-DSS |
| **Dados de saude** | Diagnosticos, medicamentos | Regulacao especifica |

```csharp
// ERRADO
_logger.LogInformation("Usuario {Email} fez login com senha {Password}", email, password);

// CORRETO
_logger.LogInformation("Usuario {UserId} fez login com sucesso", userId);
```

### 6.4 Log sampling em alta escala

Em servicos com alto throughput (milhares de req/s), logar 100% das requisicoes pode ser inviavel. Estrategias:

1. **Nivel de log dinamico**: elevar para `Warning` em producao, reduzir sob demanda
2. **Sampling no Collector**: o OTel Collector pode dropar logs via processors
3. **Logging condicional**: logar detalhes apenas quando ha erro ou latencia alta
4. **Rate limiting**: limitar logs por segundo via middleware customizado

```csharp
// Exemplo: log detalhado apenas para erros
if (response.StatusCode >= 400)
{
    _logger.LogWarning("Requisicao falhou: {StatusCode} para {Path} com body {Body}",
        response.StatusCode, request.Path, requestBody);
}
else
{
    _logger.LogInformation("Requisicao concluida: {StatusCode} para {Path}",
        response.StatusCode, request.Path);
}
```

---

## 7. Referencias

| Recurso | Link |
|---------|------|
| Loki - Documentacao oficial | https://grafana.com/docs/loki/latest/ |
| LogQL - Referencia | https://grafana.com/docs/loki/latest/logql/ |
| .NET Logging - Documentacao | https://learn.microsoft.com/dotnet/core/extensions/logging |
| OpenTelemetry Logs Specification | https://opentelemetry.io/docs/specs/otel/logs/ |
| OpenTelemetry .NET Logs | https://opentelemetry.io/docs/languages/dotnet/exporters/#logs |
| Structured Logging in ASP.NET Core | https://learn.microsoft.com/aspnet/core/fundamentals/logging |
