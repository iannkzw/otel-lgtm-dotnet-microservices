# Documentacao: Observabilidade & Stack LGTM

Guia completo de Observabilidade e da stack LGTM (Loki, Grafana, Tempo, Mimir/Prometheus), do basico ao avancado, com exemplos praticos desta PoC de microservicos .NET.

## Indice

| # | Documento | Descricao |
|---|-----------|-----------|
| 01 | [Fundamentos de Observabilidade](01-fundamentos-observabilidade.md) | 3 pilares, Golden Signals, SLI/SLO/SLA |
| 02 | [Metricas](02-metricas.md) | Tipos, PromQL, analise e boas praticas |
| 03 | [Distributed Tracing](03-distributed-tracing.md) | Spans, propagacao, sampling, Tempo |
| 04 | [Logging Estruturado](04-logging-estruturado.md) | Logs estruturados, correlacao, LogQL, Loki |
| 05 | [OpenTelemetry](05-opentelemetry.md) | Arquitetura, instrumentacao .NET, Collector, Exemplars |
| 06 | [Stack LGTM](06-stack-lgtm.md) | Loki, Grafana, Tempo, Prometheus — visao geral |
| 07 | [Grafana: Dashboards e Alertas](07-grafana-dashboards-alertas.md) | Panels, provisioning, alerting, exemplars |
| 08 | [Padroes Arquiteturais](08-padroes-arquiteturais-observabilidade.md) | Outbox+CDC, consumer lag, correlation IDs |
| 09 | [Guia Pratico](09-guia-pratico.md) | Quick start, geracao de carga, troubleshooting |

## Sobre esta PoC

Arquitetura de 3 microservicos .NET em pipeline event-driven:

```
OrderService (HTTP API)
    |-- [Outbox + Debezium CDC] --> Kafka "orders"
        |-- ProcessingWorker (Consumer + HTTP enrichment)
            |-- Kafka "notifications"
                |-- NotificationWorker (Consumer + Persistencia)
```

Stack de observabilidade: **OpenTelemetry** (instrumentacao) + **LGTM** (backend).

## Como usar esta documentacao

- **Iniciante:** Comece pelo documento 01 e siga a ordem
- **Intermediario:** Va direto ao topico de interesse
- **Avancado:** Foque nos documentos 05, 08 e nas secoes "Avancado" de cada doc

Cada documento e auto-contido, com referencias cruzadas quando necessario.
