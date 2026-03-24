# Spec — exemplars-metricas-traces

**Status:** Especificado
**Milestone:** M5 — Correlação Observabilidade
**Criado em:** 2026-03-23

---

## Problem Statement

Os três pilares de observabilidade (traces, metrics, logs) estão operacionais e verificáveis individualmente no Grafana, mas não existe **correlação automática métricas → traces**. Ao observar um pico de P95 no painel de latência do `OrderService`, o engenheiro precisa:

1. Copiar manualmente o intervalo de tempo relevante
2. Navegar até o datasource Tempo
3. Buscar traces por serviço e janela de tempo
4. Tentar correlacionar visualmente qual trace causou o pico

Com **Exemplars**, cada bucket do histograma carrega um `traceId` embutido. O Grafana gera um link direto do ponto de dados no painel PromQL para o trace causador no Tempo, reduzindo o MTTR e tornando a demo de observabilidade completa de ponta a ponta.

---

## Goals

1. Habilitar `ExemplarFilter` no OTel SDK .NET nos 3 serviços para que os histogramas emitam exemplars
2. Confirmar que o pipeline do OTel Collector não filtra/descarta exemplars (zero mudanças ou mudança mínima)
3. Configurar o datasource Prometheus do Grafana com `exemplarTraceIdDestinations` apontando para o datasource Tempo
4. Validar o fluxo ponta a ponta: painel P95 → ícone de exemplar → trace no Tempo

---

## User Stories

### US-01 — Engenheiro investiga pico de latência
**Como** engenheiro de plantão,
**quero** clicar em um ponto de P95 elevado no painel do Grafana,
**para** saltar diretamente para o trace do Tempo que causou aquela latência, sem busca manual.

### US-02 — Demo da PoC
**Como** apresentador da PoC,
**quero** demonstrar a correlação métricas → traces com um único clique no Grafana,
**para** ilustrar o valor da observabilidade distribuída com OpenTelemetry.

---

## Scope

### In Scope
- Habilitar `ExemplarFilter` (`AlwaysOn`) em `WithMetrics()` nos 3 `OtelExtensions.cs`
- Criar arquivo de provisioning de datasource Grafana com `exemplarTraceIdDestinations` → Tempo
- Adicionar bind mount do novo arquivo de datasource no `docker-compose.yaml`
- Verificar que `otelcol.yaml` não precisa de alteração (pass-through default)
- Verificar que a imagem `grafana/otel-lgtm` já suporta exemplar storage no Prometheus interno

### Out of Scope
- Novos painéis ou novas métricas
- Exemplars em counters ou gauges (padrão OTel: exemplars apenas em histogramas)
- Alterações em traces, logs ou regras de alerta existentes
- Sampling de exemplars por critérios customizados (apenas AlwaysOn)

---

## Histogramas Alvo

| Serviço | Métrica | Arquivo de Métricas |
|---|---|---|
| OrderService | `orders.create.duration` | `src/OrderService/Metrics/OrderMetrics.cs` |
| ProcessingWorker | `orders.processing.duration` | `src/ProcessingWorker/Metrics/ProcessingMetrics.cs` |
| NotificationWorker | `notifications.persistence.duration` | `src/NotificationWorker/Metrics/NotificationMetrics.cs` |

---

## Edge Cases

| Caso | Comportamento esperado |
|---|---|
| Medição sem span ativo no contexto | `.AlwaysOn` registra exemplar somente se há trace context; sem context, nenhum exemplar é emitido (comportamento padrão SDK) |
| OTel Collector recebe métricas sem exemplars (serviço antigo) | Pipeline continua funcionando normalmente; exemplars são opcionais por ponto |
| Prometheus/Mimir interno do `otel-lgtm` não tem `--enable-feature=exemplar-storage` | Exemplars chegam mas não são armazenados; o painel não exibirá ícones — isso precisará ser verificado na validação |
| Datasource UID do Prometheus no `otel-lgtm` difere do esperado | O arquivo de provisioning não surtirá efeito; verificar UID com `GET /api/datasources` no Grafana |
| Exemplar `traceId` aponta para trace já expirado no Tempo | O link abre o Tempo mas não encontra o trace — comportamento esperado de retenção |

---

## Success Criteria

1. Painel P95 do `OrderService` exibe ícone de exemplar (diamante/ponto destacado) nos pontos de dados
2. Clicar no exemplar abre o trace correspondente no Grafana Tempo
3. `dotnet build otel-poc.sln` (em container SDK 10) passa com 0 erros e 0 warnings novos
4. Nenhuma métrica existente é removida ou renomeada
5. Logs e traces continuam visíveis no Grafana após restart da stack

---

## Dependencies

- Feature `exportacao-logs-otlp` — DONE (OtelExtensions.cs já estão estruturados para extensão)
- `grafana/otel-lgtm:latest` — imagem deve ter Prometheus com suporte a exemplar storage
- OTel SDK .NET — `OpenTelemetry.Metrics` já inclui `ExemplarFilter` (sem novo pacote NuGet necessário)
