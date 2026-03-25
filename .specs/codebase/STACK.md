# Tech Stack

**Analisado em:** 2026-03-19

## Core

- **Framework:** .NET 10 com ASP.NET Core Minimal API e Worker Services
- **SDK:** 10.0.100 definido em `global.json`
- **Target Framework:** `net10.0` centralizado em `Directory.Build.props`
- **Linguagem principal:** C# com nullable e implicit usings habilitados
- **Gerenciador de dependências:** NuGet via arquivos `.csproj`
- **Orquestração local:** Docker Compose

## Serviços de Aplicação

- **OrderService:** API HTTP de entrada para criação e consulta de pedidos; publica via Outbox + CDC (sem producer Kafka direto)
- **ProcessingWorker:** worker Kafka que enriquece pedidos chamando o OrderService e publica notificações
- **NotificationWorker:** worker Kafka que persiste o resultado final no PostgreSQL

## Backend

- **API Style:** REST minimal API no OrderService
- **Mensageria:** Kafka com cliente `Confluent.Kafka`
- **Banco de dados:** PostgreSQL 16
- **Acesso a dados:** Entity Framework Core com `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0-preview.3`
- **Observabilidade:** OpenTelemetry 1.12.0 exportando OTLP gRPC para um collector local

## Pacotes .NET Relevantes

### OrderService

- `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.0-preview.3
- `OpenTelemetry.Extensions.Hosting` 1.12.0
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.12.0
- `OpenTelemetry.Instrumentation.AspNetCore` 1.12.0
- `OpenTelemetry.Instrumentation.Http` 1.12.0
- `OpenTelemetry.Instrumentation.EntityFrameworkCore` 1.13.0-beta.1

### ProcessingWorker

- `Confluent.Kafka` 2.5.0
- `Microsoft.Extensions.Http` 10.0.0
- `Microsoft.Extensions.Hosting` 10.0.0
- `OpenTelemetry.Extensions.Hosting` 1.12.0
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.12.0
- `OpenTelemetry.Instrumentation.Http` 1.12.0

### NotificationWorker

- `Confluent.Kafka` 2.5.0
- `Microsoft.Extensions.Hosting` 10.0.0
- `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.0-preview.3
- `OpenTelemetry.Extensions.Hosting` 1.12.0
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.12.0
- `OpenTelemetry.Instrumentation.Http` 1.12.0
- `OpenTelemetry.Instrumentation.EntityFrameworkCore` 1.13.0-beta.1

## Infraestrutura

- **Kafka:** `confluentinc/cp-kafka:7.5.0`
- **Zookeeper:** `confluentinc/cp-zookeeper:7.5.0`
- **PostgreSQL:** `postgres:16-alpine`
- **Kafka Connect / Debezium:** `debezium/connect:2.4`
- **Collector:** `otel/opentelemetry-collector-contrib:latest`
- **Observability backend:** `grafana/otel-lgtm:latest`
- **Geração sintética:** `ghcr.io/open-telemetry/opentelemetry-collector-contrib/telemetrygen:latest`
- **Webhook mock de alertas:** serviço Python customizado em `ops/alert-webhook-mock`

## Observabilidade

- **Tracing:** OpenTelemetry + Tempo
- **Metrics:** OpenTelemetry Metrics + Prometheus
- **Logs:** logs estruturados exportados via OTLP para Loki
- **Collector processors:** `memory_limiter`, `tail_sampling`, `span`, `batch`
- **Policies de sampling:** `drop-health-checks`, `keep-errors`, `sample-default`

## Protocolos e Transporte

- **HTTP host:** `localhost:8080` para o OrderService
- **HTTP interno:** `http://order-service:8080/` usado pelo ProcessingWorker
- **Kafka interno:** `kafka:9092`
- **Kafka Connect host:** `localhost:8083`; interno: `http://kafka-connect:8083`
- **PostgreSQL interno:** `postgres`
- **OTLP gRPC:** `otelcol:4317` internamente, `localhost:4317` no host
- **OTLP HTTP:** `otelcol:4318` internamente, `localhost:4318` no host

## Ferramentas de Desenvolvimento

- **Dockerfiles multi-stage** para os três serviços .NET
- **Task de build via container SDK 10** para `otel-poc.sln`
- **PowerShell** para geração de carga em `ops/load-generator/generate-orders.ps1`
- **Provisioning Grafana versionado** para dashboard e alertas

## Testing

- **Testes automatizados:** não há projetos de teste dedicados no repositório
- **Validação principal:** smoke/manual via Docker Compose, requests HTTP, inspeção em Grafana, Kafka e PostgreSQL
- **Geração de tráfego:** `telemetrygen-*` e `generate-orders.ps1`
