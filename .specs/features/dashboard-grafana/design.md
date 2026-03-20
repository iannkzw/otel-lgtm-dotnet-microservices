# Dashboard Grafana - Design

**Spec**: `.specs/features/dashboard-grafana/spec.md`
**Status**: Designed

---

## Architecture Overview

Esta feature continua estritamente no plano de visualizacao. A coleta e a
exportacao OTLP permanecem exatamente como estao hoje:

1. os tres servicos .NET seguem exportando metricas para `otelcol:4317`;
2. o `otelcol` continua encaminhando metricas para o LGTM em `http://lgtm:4318/v1/metrics`;
3. o Grafana embutido na imagem `grafana/otel-lgtm:latest` passa a carregar um
   dashboard versionado do repositorio por file provisioning.

Validacao de baseline realizada no ambiente atual:

1. o container `lgtm` nao usa os caminhos padrao `/etc/grafana`; o Grafana fica
   em `/otel-lgtm/grafana`;
2. o diretorio real de provisioning e
   `/otel-lgtm/grafana/conf/provisioning`;
3. o datasource Prometheus ja existe em
   `/otel-lgtm/grafana/conf/provisioning/datasources/grafana-datasources.yaml`
   com `name: Prometheus` e `uid: prometheus`;
4. os dashboards embarcados atuais sao carregados por file provisioning em
   `/otel-lgtm/grafana/conf/provisioning/dashboards/grafana-dashboards.yaml`.

Arquitetura alvo da implementacao:

1. adicionar um diretorio versionado de Grafana no repositorio;
2. montar os arquivos no servico `lgtm` com volumes read-only;
3. criar um provider proprio da PoC, separado do provider nativo da imagem;
4. apontar esse provider para um diretorio de dashboards JSON da PoC;
5. referenciar explicitamente o datasource existente `uid: prometheus` no JSON
   do dashboard, sem criar datasource novo.

---

## Design Decisions

### Provisionar por volumes read-only no `lgtm`, sem custom image

**Decision**: A implementacao deve adicionar apenas mounts no servico `lgtm` do
`docker-compose.yaml`, sem criar uma imagem derivada do LGTM.

**Reason**: O stack atual ja sobe e o Grafana da imagem suporta provisioning por
arquivo. Usar volumes reduz churn, evita rebuild desnecessario e preserva a
baseline de M2/M3.

**Trade-off**: O design fica acoplado aos caminhos internos da imagem atual
`grafana/otel-lgtm`, que sao diferentes dos caminhos padrao do Grafana.

### Criar provider proprio da PoC, em arquivo separado do provider nativo

**Decision**: A feature deve adicionar um novo arquivo YAML de provider em vez
de editar ou sobrescrever `grafana-dashboards.yaml` da imagem.

**Reason**: Isso isola o dashboard da PoC dos dashboards embarcados do LGTM e
reduz risco de regressao visual sobre assets mantidos pela imagem base.

**Trade-off**: A implementacao passa a gerenciar um arquivo adicional de
provisioning, mas ganha isolamento e clareza.

### Reutilizar o datasource Prometheus existente pelo UID `prometheus`

**Decision**: O JSON do dashboard deve referenciar o datasource Prometheus pelo
UID estavel `prometheus`, validado no runtime atual, e nao por datasource ad hoc
criado pela feature.

**Reason**: O UID foi confirmado no container do LGTM e reduz o risco de
painel vazio por ambiguidade de nome ou por diferencas futuras de exibicao no
UI.

**Trade-off**: A implementacao continua dependente da imagem LGTM manter esse
UID provisionado; a validacao final da feature precisa rechecá-lo no ambiente.

### Um dashboard overview unico, sem variaveis no MVP

**Decision**: O recorte minimo sera um unico dashboard overview da PoC, com
secoes fixas por servico e sem variaveis de template nesta iteracao.

**Reason**: O escopo de M3 pede reprodutibilidade e legibilidade rapida, nao um
framework generico de dashboards reutilizaveis.

**Trade-off**: O dashboard fica menos flexivel para filtros dinamicos por
servico, resultado ou intervalo customizado de consulta.

### Dashboard provisionado deve ser tratado como artefato versionado e imutavel

**Decision**: O provider da PoC deve usar `allowUiUpdates: false` e manter o
dashboard como fonte de verdade no repositorio.

**Reason**: A feature busca reproducibilidade. Edicoes manuais no UI criariam
drift entre ambiente e repositorio.

**Trade-off**: Ajustes visuais rapidos no Grafana precisarao voltar para o JSON
versionado em vez de permanecer somente no UI.

### Queries devem partir apenas das series normalizadas ja validadas

**Decision**: Todas as queries PromQL devem usar exclusivamente as series
normalizadas observadas no backend LGTM/Prometheus, incluindo os histograms com
`_duration_milliseconds_*`.

**Reason**: O objetivo e transformar a baseline validada de metricas em
visualizacao, nao reinterpretar nomes canonicos do codigo nem introduzir novas
metricas derivadas da aplicacao.

**Trade-off**: O design passa a depender explicitamente da camada de
normalizacao do backend Prometheus.

---

## Proposed File Layout

Estrutura esperada no repositorio para a implementacao:

```text
grafana/
  dashboards/
    otel-poc-overview.json
  provisioning/
    dashboards/
      otel-poc-dashboards.yaml
```

Mapeamento esperado no container `lgtm`:

```text
./grafana/dashboards/otel-poc-overview.json
  -> /otel-lgtm/dashboards/otel-poc-overview.json

./grafana/provisioning/dashboards/otel-poc-dashboards.yaml
  -> /otel-lgtm/grafana/conf/provisioning/dashboards/otel-poc-dashboards.yaml
```

Este layout evita montar arquivos diretamente sobre os dashboards nativos da
imagem e deixa claro o limite entre artefato versionado da PoC e baseline do
LGTM.

---

## Provisioning Strategy

### Provider YAML

O arquivo `otel-poc-dashboards.yaml` deve criar um provider de dashboards com
estas caracteristicas:

1. `apiVersion: 1`;
2. `type: file`;
3. `orgId: 1`;
4. `folder: OTel PoC`;
5. `disableDeletion: false` para manter o estado coerente com o repositorio;
6. `allowUiUpdates: false` para evitar drift manual;
7. `updateIntervalSeconds: 30` ou valor equivalente baixo, suficiente para
   recarregar JSON sem reinvencao de fluxo;
8. `options.path: /otel-lgtm/dashboards`;
9. `foldersFromFilesStructure: false`.

Exemplo estrutural esperado:

```yaml
apiVersion: 1

providers:
  - name: OTel PoC
    orgId: 1
    folder: OTel PoC
    type: file
    disableDeletion: false
    allowUiUpdates: false
    updateIntervalSeconds: 30
    options:
      path: /otel-lgtm/dashboards
      foldersFromFilesStructure: false
```

### Dashboard JSON

O JSON `otel-poc-overview.json` deve ser exportado/versionado com os seguintes
atributos conceituais:

1. `uid` estavel, por exemplo `otel-poc-m3-overview`;
2. `title` estavel, por exemplo `OTel PoC - Service Metrics`;
3. tags leves como `otel-poc`, `m3` e `metrics`;
4. datasource fixado no objeto `uid: prometheus` em todos os targets;
5. time range inicial curto para demo, como ultimos 15 ou 30 minutos;
6. refresh automatico moderado, como `30s`, coerente com a natureza da PoC;
7. layout organizado por secoes de servico, sem mistura com dashboards nativos.

### Compose Change Surface

Na implementacao, o unico servico que deve receber mudanca funcional e o
`lgtm`, apenas com volumes adicionais read-only. Nenhum servico .NET, nenhum
topic Kafka, nenhum schema Postgres e nenhum processor do collector precisam ser
alterados para esta feature.

---

## Dashboard Layout

O dashboard minimo deve ter 3 secoes, uma para cada servico, com 9 paineis no
total.

### Row 1: OrderService

1. Throughput de criacao por `result` em time series.
2. Latencia P50/P95 de criacao em time series unico com duas consultas.
3. Backlog atual por `status` em bar gauge ou stat orientado a comparacao.

### Row 2: ProcessingWorker

1. Throughput de processamento por `result` em time series.
2. Latencia P50/P95 de processamento em time series unico com duas consultas.
3. Consumer lag agregado do topic `orders` em stat com sparkline ou time
   series simples.

### Row 3: NotificationWorker

1. Throughput de persistencia por `result` em time series.
2. Latencia P50/P95 de persistencia em time series unico com duas consultas.
3. Consumer lag agregado do topic `notifications` em stat com sparkline ou time
   series simples.

Regras visuais do MVP:

1. throughput e latencia ficam sempre no mesmo row do servico correspondente;
2. backlog e lag ficam como terceiro painel de cada row para leitura operacional;
3. nao incluir paines de alertas, links para traces, annotations, playlists,
   tabelas por particao ou variaveis de template nesta iteracao;
4. tratar `service_name`, `job` e `service_instance_id` como filtros de backend,
   nao como variaveis customizadas obrigatorias do dashboard.

---

## Query Design

### Regras gerais

1. usar `service_name` como filtro principal de servico;
2. preservar apenas labels validadas de baixa cardinalidade: `result`, `status`,
   `topic` e `consumer_group`;
3. usar janela padrao de `5m` para `rate(...)` e `histogram_quantile(...)`;
4. calcular percentis sempre a partir de `*_bucket`, nao de media por `_sum/_count`;
5. nao criar queries por `service_instance_id`, `orderId`, `traceId` ou particao.

### OrderService

Throughput:

```promql
sum by (result) (
  rate(orders_created_total{service_name="order-service"}[5m])
)
```

Latencia P50:

```promql
histogram_quantile(
  0.50,
  sum by (le) (
    rate(orders_create_duration_milliseconds_bucket{service_name="order-service",result="created"}[5m])
  )
)
```

Latencia P95:

```promql
histogram_quantile(
  0.95,
  sum by (le) (
    rate(orders_create_duration_milliseconds_bucket{service_name="order-service",result="created"}[5m])
  )
)
```

Backlog:

```promql
sum by (status) (
  orders_backlog_current{service_name="order-service"}
)
```

### ProcessingWorker

Throughput:

```promql
sum by (result) (
  rate(orders_processed_total{service_name="processing-worker"}[5m])
)
```

Latencia P50:

```promql
histogram_quantile(
  0.50,
  sum by (le) (
    rate(orders_processing_duration_milliseconds_bucket{service_name="processing-worker",result="processed"}[5m])
  )
)
```

Latencia P95:

```promql
histogram_quantile(
  0.95,
  sum by (le) (
    rate(orders_processing_duration_milliseconds_bucket{service_name="processing-worker",result="processed"}[5m])
  )
)
```

Lag:

```promql
sum by (topic, consumer_group) (
  kafka_consumer_lag{service_name="processing-worker",topic="orders"}
)
```

### NotificationWorker

Throughput:

```promql
sum by (result) (
  rate(notifications_persisted_total{service_name="notification-worker"}[5m])
)
```

Latencia P50:

```promql
histogram_quantile(
  0.50,
  sum by (le) (
    rate(notifications_persistence_duration_milliseconds_bucket{service_name="notification-worker",result="persisted"}[5m])
  )
)
```

Latencia P95:

```promql
histogram_quantile(
  0.95,
  sum by (le) (
    rate(notifications_persistence_duration_milliseconds_bucket{service_name="notification-worker",result="persisted"}[5m])
  )
)
```

Lag:

```promql
sum by (topic, consumer_group) (
  kafka_consumer_lag{service_name="notification-worker",topic="notifications"}
)
```

---

## Datasource Binding Strategy

Para reduzir risco de paineis vazios, o JSON do dashboard deve usar a referencia
de datasource compativel com provisioning moderno do Grafana, isto e, objeto com
`type: prometheus` e `uid: prometheus` em cada target relevante.

Diretrizes:

1. nao depender apenas do nome textual `Prometheus` quando o exporter do JSON
   permitir referencia por UID;
2. nao criar arquivo novo de datasource da PoC nesta feature;
3. nao alterar `grafana-datasources.yaml` da imagem, pois o datasource atual ja
   atende ao dashboard;
4. validar na implementacao que o import/export final do JSON preservou o UID
   corretamente e nao voltou para uma referencia local temporaria do UI.

---

## Validation Strategy

### Validacoes de estrutura

1. confirmar que os volumes do `docker-compose.yaml` montam arquivos em
   `/otel-lgtm/grafana/conf/provisioning/dashboards` e `/otel-lgtm/dashboards`;
2. confirmar que o provider YAML da PoC nao sobrescreve o provider nativo do LGTM;
3. confirmar que o JSON versionado referencia `uid: prometheus`.

### Validacoes funcionais

1. subir ou reiniciar o servico `lgtm` com os novos volumes;
2. abrir o Grafana e confirmar que a pasta `OTel PoC` e o dashboard aparecem sem
   criacao manual;
3. abrir cada painel e conferir que nao ha erro de datasource nao encontrado;
4. comparar as queries do dashboard com o Explore usando as mesmas series;
5. validar que o dashboard continua funcional apos `docker compose up -d` em
   ambiente limpo, sem ajustes manuais.

### Validacoes de nao-regressao

1. nenhum arquivo de aplicacao .NET deve mudar nesta feature;
2. `otelcol.yaml` e processors existentes devem permanecer intactos;
3. traces, logs, contratos Kafka e persistencia devem continuar exatamente como
   na baseline de M2/M3.

---

## Risks And Mitigations

### Caminho interno nao padrao do Grafana no LGTM

**Risk**: Implementar mounts em `/etc/grafana/...` quebraria o provisionamento,
porque a imagem atual usa `/otel-lgtm/grafana/conf/provisioning`.

**Mitigation**: Tratar os caminhos validados no runtime como fonte de verdade da
implementacao.

### Drift do datasource no JSON exportado

**Risk**: O Grafana pode exportar um JSON com referencia de datasource diferente
da esperada, levando a paineis vazios no ambiente reprovisionado.

**Mitigation**: Revisar o JSON final antes de versionar e padronizar o uso do
UID `prometheus`.

### Divergencia entre queries de histogram e series reais do backend

**Risk**: Usar nomes sem o sufixo `_milliseconds_bucket` zeraria os percentis.

**Mitigation**: Revalidar os nomes no Explore/Prometheus durante a implementacao
e manter o catalogo deste design como baseline.

### Mistura entre dashboard da PoC e dashboards nativos da imagem

**Risk**: Editar o provider nativo ou gravar o JSON junto aos dashboards base da
imagem aumentaria o risco de colisao, troubleshooting confuso e manutencao ruim.

**Mitigation**: Manter provider e diretorio da PoC separados e explicitamente
nomeados.

---

## Implementation Slices For Next Tasks

1. criar a estrutura `grafana/dashboards` e `grafana/provisioning/dashboards`;
2. adicionar provider YAML proprio da PoC apontando para `/otel-lgtm/dashboards`;
3. ajustar `docker-compose.yaml` para montar os artefatos no servico `lgtm`;
4. criar/exportar o JSON inicial do dashboard com UID estavel e pasta `OTel PoC`;
5. montar os 9 paineis minimos com as queries PromQL deste design;
6. validar o binding do datasource `uid: prometheus` em todos os paineis;
7. subir o stack, confirmar auto-provisionamento e comparar consultas no Explore;
8. fechar a iteracao sem alterar servicos .NET, collector ou contratos da PoC.