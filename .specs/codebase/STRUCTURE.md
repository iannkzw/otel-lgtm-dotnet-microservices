# Project Structure

## Árvore Principal

```text
otel-lgtm-dotnet-microservices/
├── .specs/
│   ├── codebase/
│   ├── features/
│   └── project/
├── grafana/
│   ├── dashboards/
│   └── provisioning/
├── processors/
│   └── sampling/
├── src/
│   ├── NotificationWorker/
│   ├── OrderService/
│   ├── ProcessingWorker/
│   └── Shared/
├── tools/
│   ├── alert-webhook-mock/
│   └── load-generator/
├── Directory.Build.props
├── docker-compose.yaml
├── global.json
├── otel-poc.sln
├── otelcol.yaml
└── README.md
```

## Organização por Área

### `.specs/`

**Propósito:** documentação viva usada pela abordagem spec-driven.

- `codebase/`: mapeamento brownfield do estado atual do repositório.
- `project/`: visão e memória do projeto.
- `features/`: especificações de features implementadas ou planejadas.

### `src/`

**Propósito:** código fonte principal da PoC.

#### `src/OrderService/`

- Serviço HTTP de entrada.
- Contém API, entidade `Order`, entidade `OutboxMessage`, DbContext e Dockerfile.

#### `src/ProcessingWorker/`

- Worker assíncrono intermediário.
- Contém cliente HTTP interno, consumer Kafka, publisher de notificações, métricas de processamento e Dockerfile.

#### `src/NotificationWorker/`

- Worker final de persistência.
- Contém consumer Kafka, bootstrap explícito de schema, entidade `PersistedNotification`, métricas e Dockerfile.

#### `src/Shared/`

- Código compartilhado de propagação W3C de trace context.

### `grafana/`

**Propósito:** provisionamento versionado de operação.

- `dashboards/otel-poc-overview.json`: dashboard da PoC.
- `provisioning/dashboards/`: configuração do provisioning do dashboard.
- `provisioning/alerting/`: regras, contact points e policy tree de alertas.

### `processors/`

**Propósito:** modularizar políticas do collector.

- `sampling/`: políticas de tail sampling consumidas por `otelcol.yaml`.

### `tools/`

**Propósito:** utilitários operacionais para demonstração e validação.

- `alert-webhook-mock/`: servidor Python que recebe alertas do Grafana e expõe `/health` e `/requests`.
- `debezium/`: configuração do conector Debezium (`order-outbox-connector.json`).
- `load-generator/`: script PowerShell para gerar carga contra o OrderService.
- `postgres/`: scripts SQL de inicialização do banco (`init.sql`) montados via `docker-entrypoint-initdb.d`.

## Onde as Coisas Ficam

### Aplicação e Fluxo de Negócio

- API HTTP: `src/OrderService/Program.cs`
- Consumidor intermediário: `src/ProcessingWorker/Worker.cs`
- Consumidor final: `src/NotificationWorker/Worker.cs`
- Contratos de entrada e eventos: `Contracts/` dentro de cada serviço

### Persistência

- Entidade e mapping de pedidos: `src/OrderService/Data/`
- Entidade e mapping de notificações: `src/NotificationWorker/Data/`
- String de conexão: variáveis de ambiente no `docker-compose.yaml`

### Observabilidade

- Bootstrap OpenTelemetry por serviço: `src/*/Extensions/OtelExtensions.cs`
- Collector: `otelcol.yaml`
- Sampling: `processors/sampling/`
- Dashboards e alertas: `grafana/`

### Infraestrutura Local

- Compose completo da demo: `docker-compose.yaml`
- Configuração compartilhada .NET: `Directory.Build.props`
- SDK alvo: `global.json`
- Solução .NET: `otel-poc.sln`

## Padrão Interno dos Serviços

Os três projetos em `src/` seguem uma estrutura consistente:

- `Program.cs` para bootstrap
- `appsettings*.json` para config local
- `Dockerfile` para build e runtime
- `Contracts/` para DTOs e eventos
- `Extensions/` para extensão transversal
- `Messaging/` para Kafka e trace propagation *(apenas ProcessingWorker e NotificationWorker — o OrderService não possui esta pasta)*
- `Metrics/` para medição operacional

Áreas adicionais aparecem conforme a responsabilidade do serviço:

- `Data/` nos serviços com persistência
- `Clients/` no ProcessingWorker para dependência HTTP interna

## Artefatos Operacionais Versionados

- `README.md` é o roteiro canônico da demo local.
- O dashboard Grafana e o provisionamento de alertas fazem parte do repositório, então o ambiente sobe já observável.
- O mock de webhook e o gerador de carga permitem validar alertas e volume sem depender de ferramentas externas.
