# Testing Infrastructure

## Situação Atual

O repositório não possui projetos formais de teste automatizado, como xUnit, NUnit, MSTest ou suites E2E dedicadas. A validação atual é predominantemente operacional, baseada em:

- build da solução e dos projetos dentro de container SDK .NET 10;
- subida do ambiente completo via Docker Compose;
- execução manual de requests e geração de carga;
- inspeção de dados em Kafka, PostgreSQL, Grafana e logs.

## Estratégia Real de Validação

### 1. Build e Bootstrapping

- Existe task de VS Code para build da solução com container `mcr.microsoft.com/dotnet/sdk:10.0`.
- Cada serviço também pode ser construído isoladamente via `docker run ... dotnet build`.
- O fluxo principal validado no repositório é `docker compose up -d --build`.

### 2. Smoke Test Manual do Fluxo Feliz

O roteiro principal do `README.md` valida o caminho ponta a ponta:

1. chamar `POST /orders` no host;
2. consultar opcionalmente `GET /orders/{id}`;
3. deixar o fluxo seguir por Kafka até o ProcessingWorker e NotificationWorker;
4. confirmar traces, métricas e logs no Grafana;
5. confirmar persistência no PostgreSQL e consumo nos tópicos quando necessário.

### 3. Geração de Carga Versionada

**Arquivo:** `ops/load-generator/generate-orders.ps1`

O script suporta dois modos:

- `happy`: carga sequencial para popular a demo sem pressão extra.
- `latency`: carga concorrente com `-Concurrency` para induzir maior latência e exercitar alertas.

Parâmetros relevantes:

- `BaseUrl`
- `Count`
- `Mode`
- `Concurrency`
- `TimeoutSeconds`
- `PauseMs`

O script mede duração, captura status HTTP e tenta extrair `orderId` da resposta.

### 4. Telemetrygen

O `docker-compose.yaml` inclui:

- `telemetrygen-traces`
- `telemetrygen-logs`
- `telemetrygen-metrics`

Esses containers geram sinais sintéticos adicionais para validar o collector e a stack LGTM mesmo sem tráfego de negócio.

### 5. Validação de Observabilidade

**Traces:** inspeção via Tempo no Grafana.

**Métricas:** inspeção via Prometheus no dashboard `OTel PoC - Service Metrics` e no Explore.

**Logs:** inspeção via Loki e `docker compose logs`.

**Alertas:** verificação via provisioning do Grafana e recebimento no `alert-webhook-mock`.

## Cobertura Implícita por Componente

### OrderService

- validação de payload vazio ou whitespace;
- persistência inicial em PostgreSQL;
- publicação Kafka;
- atualização de status do pedido;
- health endpoint em `/health`.

### ProcessingWorker

- consumo Kafka;
- propagação de contexto distribuído;
- enriquecimento HTTP via OrderService;
- classificação de falhas: `invalid_payload`, `not_found`, `http_error`, `timeout`, `network_error`, `publish_failed`, `unexpected_error`.

### NotificationWorker

- consumo Kafka;
- validação detalhada do payload recebido;
- persistência final em PostgreSQL;
- deduplicação curta de falhas de consumo em métricas;
- bootstrap de schema da tabela `notification_results`.

## Testabilidade e Instrumentação Existente

Apesar da ausência de testes automatizados, a codebase já possui pontos que favorecem futura automação:

- contratos de entrada e eventos pequenos e isolados;
- clients e publishers separados por interface;
- workers com responsabilidades relativamente concentradas;
- métricas com resultados categorizados, úteis para asserts operacionais;
- ambiente local totalmente reproduzível via compose.

## Lacunas Atuais

- não há testes unitários para validação, publishers, DbContexts ou helpers de tracing;
- não há testes de integração automatizados para Kafka/PostgreSQL;
- não há suite automatizada de smoke/E2E que suba o compose, gere pedidos e verifique estado final;
- a validação atual depende de inspeção manual ou comandos ad hoc.

## Melhor Próximo Passo

A evolução mais valiosa para a PoC é adicionar uma suite de smoke/integration automatizada que:

1. suba o compose ou reutilize o ambiente local;
2. crie pedidos reais no OrderService;
3. aguarde o processamento assíncrono completo;
4. valide estado final em PostgreSQL;
5. valide presença de métricas e traces mínimos;
6. possa rodar em CI.
