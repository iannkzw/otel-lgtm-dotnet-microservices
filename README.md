# OTel PoC com .NET, Kafka, PostgreSQL e LGTM

Esta PoC demonstra um fluxo distribuído ponta a ponta com três serviços .NET, propagação de contexto por Kafka, persistência em PostgreSQL e observabilidade completa via OpenTelemetry Collector e stack LGTM.

O caminho principal da demo é:

1. `POST /orders` no `order-service`.
2. Persistência inicial no PostgreSQL.
3. Publicação no topic Kafka `orders`.
4. Consumo no `processing-worker`, consulta HTTP ao `order-service` e publicação em `notifications`.
5. Consumo no `notification-worker` e persistência final no PostgreSQL.
6. Exportação de traces, métricas e logs para `otelcol` e visualização no Grafana, Tempo, Prometheus e Loki.

## Visão Geral da Stack

| Componente | Papel na PoC | Acesso principal |
| --- | --- | --- |
| `order-service` | API HTTP de entrada e publicação inicial no Kafka | Host: `http://localhost:8080` |
| `processing-worker` | Consome `orders`, chama o `order-service` e publica em `notifications` | Rede interna Docker |
| `notification-worker` | Consome `notifications` e persiste o resultado final | Rede interna Docker |
| `postgres` | Banco compartilhado da PoC | Rede interna Docker |
| `kafka` | Backbone de eventos entre os serviços | Rede interna Docker |
| `kafka-ui` | Interface visual para tópicos, consumer groups e mensagens do Kafka | Host: `http://localhost:8085` |
| `zookeeper` | Coordenação do Kafka | Rede interna Docker |
| `otelcol` | Recebe OTLP e encaminha sinais para a stack LGTM | Host: `localhost:4317` e `localhost:4318` |
| `lgtm` | Grafana, Tempo, Loki e Prometheus em um único container | Host: `http://localhost:3000` |
| `alert-webhook-mock` | Receiver local de alertas do Grafana | Rede interna Docker |

O dashboard e os alertas da PoC já fazem parte da baseline validada do repositório. O compose da raiz é o caminho primário de bootstrap e demonstração local.

## Pré-requisitos

- Docker Desktop, ou ambiente Docker equivalente, com suporte a `docker compose`.
- Portas `3000`, `8080`, `8085`, `4317` e `4318` livres no host.
- Shell local para executar comandos `docker compose` e requests HTTP.

Observações:

- A execução principal da PoC não depende de .NET 10 SDK instalado no host.
- Builds locais fora do Docker são opcionais e dependem do ambiente da máquina.

## Bootstrap do Ambiente

Suba todo o ambiente com build das imagens locais:

```powershell
docker compose up -d --build
```

Verifique o estado inicial dos containers:

```powershell
docker compose ps
```

Cheque rapidamente se a API está respondendo no host:

```powershell
Invoke-WebRequest -UseBasicParsing http://localhost:8080/health | Select-Object -ExpandProperty Content
```

Resultado esperado, em alto nível:

- `lgtm`, `otelcol`, `kafka`, `kafka-ui`, `postgres`, `order-service`, `processing-worker`, `notification-worker` e `alert-webhook-mock` aparecem iniciados.
- O Grafana pode levar alguns instantes para provisionar dashboard e alertas após a primeira subida.
- `order-service` responde no host, enquanto workers, Kafka, PostgreSQL e webhook operam apenas na rede Docker.

Se precisar de diagnóstico rápido durante a subida:

```powershell
docker compose logs --no-color --tail=50 lgtm
docker compose logs --no-color --tail=50 otelcol
docker compose logs --no-color --tail=50 order-service
```

## Matriz Host versus Rede Interna

Use a tabela abaixo para evitar confundir URLs do host com endpoints internos do compose.

| Recurso | Host | Rede interna Docker | Observação |
| --- | --- | --- | --- |
| Grafana / LGTM | `http://localhost:3000` | `http://lgtm:3000` | Use `localhost` a partir do host |
| OrderService | `http://localhost:8080` | `http://order-service:8080` | O `processing-worker` usa o endpoint interno |
| Kafka UI | `http://localhost:8085` | `http://kafka-ui:8080` | Interface visual para tópicos e consumer groups |
| OTLP gRPC | `localhost:4317` | `http://otelcol:4317` | Exportação dos serviços para o collector |
| OTLP HTTP | `localhost:4318` | `http://otelcol:4318` | Exposto no host para inspeção e testes |
| Kafka | não exposto | `kafka:9092` | Uso exclusivo entre containers |
| PostgreSQL | não exposto | `postgres` | Uso exclusivo entre containers |
| Zookeeper | não exposto | `zookeeper:2181` | Uso exclusivo entre containers |
| ProcessingWorker | não exposto | container interno | Sem endpoint HTTP publicado |
| NotificationWorker | não exposto | container interno | Sem endpoint HTTP publicado |
| Alert Webhook Mock | não exposto | `http://alert-webhook-mock:8080` | Não existe URL equivalente em `localhost` |

Ponto importante:

- O `alert-webhook-mock` não está exposto no host. Para validar alertas, use logs do compose ou inspeção interna do container. Não tente abrir `http://localhost:8080/requests` para esse serviço.

## Fluxo Feliz da Demo

Crie um pedido pelo host com PowerShell e capture o `orderId` retornado:

```powershell
$payload = @{ description = "demo-" + [guid]::NewGuid().ToString() } | ConvertTo-Json -Compress
$order = Invoke-RestMethod -Uri http://localhost:8080/orders -Method POST -ContentType 'application/json' -Body $payload
$orderId = $order.orderId
$orderId
```

Opcionalmente, consulte o estado persistido do pedido:

```powershell
Invoke-RestMethod -Uri "http://localhost:8080/orders/$orderId" | ConvertTo-Json -Depth 5
```

Com isso, o restante do fluxo segue automaticamente pela baseline da PoC:

1. `order-service` persiste o pedido e publica no topic `orders`.
2. `processing-worker` consome, consulta `GET /orders/{id}` internamente e publica em `notifications`.
3. `notification-worker` consome e persiste o resultado final.

Para esta demo, não é necessário fazer chamadas manuais para Kafka, PostgreSQL ou workers.

## Kafka UI

Para inspeção visual do Kafka, a PoC inclui `Kafka UI`, que é hoje uma das opções mais usadas para ambiente local e diagnóstico rápido.

Acesso pelo host:

- `http://localhost:8085`

Uso principal na demo:

1. Abra a UI e selecione o cluster `otel-poc`.
2. Consulte os tópicos `orders` e `notifications`.
3. Consulte os consumer groups `processing-worker` e `notification-worker`.
4. Verifique mensagens, offsets e lag de consumo sem depender apenas de comandos no terminal.

Se quiser subir apenas a UI depois que o restante do stack já estiver no ar:

```powershell
docker compose up -d kafka-ui
```

Validação rápida:

```powershell
docker compose ps kafka-ui
```

## Gerador de Carga (Automatização Opcional)

Para popular a PoC com múltiplos pedidos de forma consistente e reproduzível, use o gerador de carga versionado:

```powershell
powershell -File .\tools\load-generator\generate-orders.ps1 -Count 20
```

Esse comando envia 20 pedidos reais contra `POST /orders` de forma sequencial (modo feliz). Os sinais resultantes (traces, métricas, logs) alimentam o dashboard e os alertas já provisionados na baseline.

Modo opcional de pressão de latência para demonstrar o alerta `OrderService P95 > 500 ms`:

```powershell
powershell -File .\tools\load-generator\generate-orders.ps1 -Count 120 -Mode latency -Concurrency 6
```

O gerador é um utilitário externo de demonstração, não um componente funcional da PoC. O caminho principal da demo continua sendo este README como roteiro canônico.

## Traces

Abra o Grafana em `http://localhost:3000` com `admin` / `admin`.

No menu lateral:

1. Abra `Explore`.
2. Selecione o datasource `Tempo`.
3. Procure pelo trace gerado logo após o `POST /orders`.

Expectativa mínima do trace distribuído:

- hop HTTP no `order-service` para `POST /orders`;
- persistência inicial do pedido;
- publicação Kafka para `orders`;
- consumo no `processing-worker`;
- chamada HTTP interna `GET /orders/{id}`;
- publicação Kafka para `notifications`;
- consumo no `notification-worker`;
- persistência final em banco.

Observação útil:

- Não use `/health` como referência principal para Tempo. Health checks bem-sucedidos são descartados pela política de sampling da baseline.

## Métricas e Dashboard

O dashboard versionado da PoC já é provisionado automaticamente no Grafana:

- Nome: `OTel PoC - Service Metrics`
- UID: `otel-poc-m3-overview`
- Pasta: `OTel PoC`

Ele consolida os sinais principais por serviço:

- `order-service`: throughput de criação, latência P50/P95 e backlog atual por status.
- `processing-worker`: throughput de processamento, latência P50/P95 e `kafka_consumer_lag` do topic `orders`.
- `notification-worker`: throughput de persistência, latência P50/P95 e `kafka_consumer_lag` do topic `notifications`.

Para inspeção ad hoc:

1. Abra `Explore` no Grafana.
2. Selecione o datasource `Prometheus`.
3. Consulte as séries que sustentam o dashboard, como `orders_created_total`, `orders_backlog_current`, `orders_processed_total`, `notifications_persisted_total` e `kafka_consumer_lag`.

## Logs

O caminho preferencial para logs é o Grafana:

1. Abra `Explore`.
2. Selecione o datasource `Loki`.
3. Correlacione os logs com a janela de tempo do pedido recém-criado e com os serviços envolvidos no fluxo.

Para diagnóstico rápido no host, use logs de container:

```powershell
docker compose logs --no-color --tail=50 order-service
docker compose logs --no-color --tail=50 processing-worker
docker compose logs --no-color --tail=50 notification-worker
```

Na prática, a correlação mais útil da demo vem da combinação entre janela temporal do pedido, nomes de serviço e contexto de trace presente nos registros estruturados.

## Alertas

As regras provisionadas da PoC podem ser vistas no Grafana em `Alerting`.

Baseline atual:

- Regra `OrderService P95 > 500 ms`
- Regra `ProcessingWorker lag > 100`
- Contact point `OTel PoC Local Webhook`

O receiver local dessas notificações é o serviço interno `alert-webhook-mock`, configurado para receber POSTs em `http://alert-webhook-mock:8080/` dentro da rede Docker.

Validação principal do receiver pelo host:

```powershell
docker compose logs --no-color --tail=50 alert-webhook-mock
```

Inspeção opcional do histórico recebido, de dentro do próprio container:

```powershell
docker compose exec -T alert-webhook-mock wget -qO- http://localhost:8080/requests
```

Pontos importantes:

- O `alert-webhook-mock` não tem porta publicada no host.
- A verificação correta é por logs do compose ou por inspeção interna do endpoint `/requests`.
- Não é necessário alterar o `docker-compose.yaml` para validar os alertas da baseline.

## Troubleshooting

### Porta em uso no host

Se `docker compose up -d --build` falhar por conflito de porta, verifique se algo já está usando `3000`, `8080`, `8085`, `4317` ou `4318`. Essas são as portas do host exigidas pela demo.

### Container ainda inicializando ou não saudável

Confirme o estado com:

```powershell
docker compose ps
docker compose logs --no-color --tail=50 kafka
docker compose logs --no-color --tail=50 postgres
docker compose logs --no-color --tail=50 order-service
```

O compose depende de health checks para Kafka e PostgreSQL antes de iniciar partes do fluxo.

### Dashboard ou alertas não apareceram imediatamente

O Grafana pode levar alguns instantes para provisionar dashboard e regras depois da subida inicial. Aguarde um pouco e, se necessário, confira:

```powershell
docker compose logs --no-color --tail=50 lgtm
```

### Sem .NET 10 SDK no host

Isso não bloqueia a demonstração principal. O caminho suportado para rodar a PoC é o compose da raiz. Use build local fora do Docker apenas se o ambiente já estiver preparado para isso.

## Artefatos de Referência da Baseline

Os passos deste README foram alinhados diretamente com os artefatos versionados da PoC:

- `docker-compose.yaml`
- `src/OrderService/Program.cs`
- `grafana/dashboards/otel-poc-overview.json`
- `grafana/provisioning/alerting/otel-poc-alert-rules.yaml`
- `grafana/provisioning/alerting/otel-poc-contact-points.yaml`
- `grafana/provisioning/alerting/otel-poc-notification-policies.yaml`
- `tools/alert-webhook-mock/server.py`