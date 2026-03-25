# OTel PoC — Observabilidade com LGTM + .NET 10

**Vision:** PoC de observabilidade com 3 microsserviços .NET 10 comunicando via Kafka e HTTP, demonstrando tracing distribuído, métricas, logs estruturados e alertas integrados à stack LGTM (Loki, Grafana, Tempo, Prometheus).

**For:** Engenheiros de plataforma e desenvolvedores backend validando a adoção de observabilidade com OpenTelemetry em arquiteturas orientadas a eventos.

**Solves:** Demonstrar de forma concreta como instrumentar microsserviços .NET com OpenTelemetry — incluindo propagação de contexto entre serviços HTTP e Kafka, instrumentação de banco de dados e configuração de alertas — usando um compose unificado e artefatos versionados de infraestrutura no próprio repositório.

---

## Goals

- **G1:** Validar propagação de trace context entre os 3 serviços (HTTP + Kafka) — verificável via trace completo no Tempo com todos os spans conectados
- **G2:** Demonstrar métricas customizadas por serviço visíveis no Prometheus/Grafana — verificável via dashboard funcional com 3+ métricas de negócio
- **G3:** Mostrar logs estruturados correlacionados ao trace (TraceId/SpanId nos logs) — verificável via query no Loki linkando log ao trace no Tempo
- **G4:** Configurar ao menos 1 alerta funcional disparando no Grafana — verificável por alerta ativo no ambiente

---

## Tech Stack

**Core:**
- Framework: ASP.NET Core 10 (Minimal API) + .NET 10 Worker Service
- Linguagem: C# 13
- Banco de dados: PostgreSQL 16
- Mensageria: Apache Kafka 3.x

**Observabilidade:**
- SDK: OpenTelemetry .NET (Traces + Metrics + Logs)
- Exporter: OTLP HTTP/gRPC → OTel Collector (`otel-demo-main`)
- Backend: Grafana LGTM (Tempo + Loki + Prometheus + Grafana)

**Key dependencies:**
- `OpenTelemetry.Extensions.Hosting`
- `OpenTelemetry.Instrumentation.AspNetCore`
- `OpenTelemetry.Instrumentation.Http`
- `OpenTelemetry.Instrumentation.EntityFrameworkCore`
- `Confluent.Kafka` + instrumentação manual de contexto Kafka
- `Npgsql.EntityFrameworkCore.PostgreSQL`

**Infraestrutura:**
- Docker Compose unificado na raiz do repositório

---

## Decisões de Design do Repositório

- O código de aplicação permanece em `src/` nesta fase para evitar churn em `.sln`, `.csproj` e Dockerfiles.
- Infraestrutura versionada fica concentrada em `infra/`, incluindo Grafana, OTel Collector, processors e bootstrap SQL do PostgreSQL.
- Utilitários operacionais ficam concentrados em `ops/`, incluindo webhook mock, configuração do Debezium e gerador de carga.
- A raiz do repositório deve permanecer enxuta, mantendo como ponto de entrada apenas os artefatos principais de bootstrap e solução.

---

## Arquitetura dos Serviços

```
┌─────────────────────────────────────────────────────────────┐
│                     [API] OrderService                      │
│  POST /orders → gera evento no Kafka + salva no Postgres    │
│  Expõe: traces HTTP, métricas de request, logs estruturados │
└──────────────────────┬──────────────────────────────────────┘
                       │ Kafka: topic "orders"
                       ▼
┌─────────────────────────────────────────────────────────────┐
│               [Worker] ProcessingWorker                     │
│  Consome "orders" → processa (mock) → publica em "notify"   │
│  Chama OrderService via HTTP para enriquecer dados          │
│  Expõe: traces Kafka consumer, call HTTP outbound, métricas │
└──────────────────────┬──────────────────────────────────────┘
                       │ HTTP GET /orders/{id}
                       │ Kafka: topic "notifications"
                       ▼
┌─────────────────────────────────────────────────────────────┐
│               [Worker] NotificationWorker                   │
│  Consome "notifications" → processa (mock log/email)        │
│  Salva resultado no Postgres                                │
│  Expõe: traces Kafka consumer, DB traces, métricas          │
└─────────────────────────────────────────────────────────────┘
```

---

## Scope

**v1 inclui:**
- `OrderService` (API): endpoint `POST /orders` e `GET /orders/{id}`, PostgreSQL, Publisher Kafka
- `ProcessingWorker`: Consumer Kafka `orders`, HTTP call para OrderService, Publisher Kafka `notifications`
- `NotificationWorker`: Consumer Kafka `notifications`, persistência no PostgreSQL
- Instrumentação OpenTelemetry completa nos 3 serviços (traces, metrics, logs)
- Propagação de trace context entre HTTP e Kafka (manual via headers)
- Dashboard Grafana básico: RED metrics por serviço + Kafka lag
- 1 alerta Grafana: latência P95 do OrderService acima de 500ms
- Docker Compose unificado como ponto único de bootstrap local

**Explicitamente fora do escopo:**
- Autenticação/autorização
- Múltiplos endpoints ou lógica de negócios real
- Schema registry / Avro / serialização estruturada
- Retry/DLQ para Kafka
- CI/CD
- Testes automatizados
- Múltiplos ambientes (staging, prod)

---

## Constraints

- **Técnico:** Preservar o compose unificado como ponto único de bootstrap e separar claramente aplicação (`src/`), infraestrutura (`infra/`) e operações (`ops/`)
- **Escopo:** Cada serviço deve ter o mínimo de lógica de negócio necessário — o foco é a instrumentação, não a aplicação
- **Runtime:** .NET 10 Preview / RC (mais recente disponível na data)
- **Serviços de infra:** Kafka + Zookeeper + PostgreSQL adicionados ao Docker Compose
