# Dashboard Grafana - Tasks

**Design**: `.specs/features/dashboard-grafana/design.md`
**Status**: Tasks Defined

---

## Execution Plan

### Phase 1: Estrutura versionada e provisioning

```text
T1 (estrutura de artefatos) -> T2 (provider YAML) -> T3 (volumes no lgtm)
```

### Phase 2: Dashboard JSON versionado

```text
T3 -> T4 (shell do dashboard)
T4 -> T5 (row OrderService)
T4 -> T6 (row ProcessingWorker)
T4 -> T7 (row NotificationWorker)
```

### Phase 3: Validacao de binding, provisionamento e queries

```text
T2 + T3 + T5 + T6 + T7 -> T8 (binding datasource + caminhos reais)
T8 -> T9 (auto-provisionamento funcional no Grafana)
T9 -> T10 (queries PromQL normalizadas + nao-regressao)
```

---

## Task Breakdown

### T1: Criar a estrutura versionada de artefatos Grafana

**What**: Introduzir no repositorio a estrutura minima de diretorios e nomes de
artefatos para isolar os assets da PoC do baseline nativo da imagem LGTM.

**Where**: `grafana/dashboards/`, `grafana/provisioning/dashboards/`

**Depends on**: feature `metricas-customizadas` concluida

**Done when**:

- [ ] Existe a estrutura `grafana/dashboards/` para os JSONs versionados da PoC
- [ ] Existe a estrutura `grafana/provisioning/dashboards/` para o provider da PoC
- [ ] Os nomes finais dos arquivos deixam claro o ownership da PoC, sem colisao com assets nativos do LGTM
- [ ] Nenhum arquivo novo de datasource e criado nesta feature
- [ ] Nenhum artefato Grafana e colocado sobre caminhos internos da imagem fora do design aprovado

**Verification**:

- Local: a estrutura aparece no workspace exatamente como prevista no design
- Runtime: nao aplicavel nesta tarefa

---

### T2: Criar o provider YAML proprio da PoC

**What**: Adicionar um provider de dashboards por arquivo separado, apontando
para o diretorio versionado da PoC e preservando o provider nativo da imagem.

**Where**: `grafana/provisioning/dashboards/otel-poc-dashboards.yaml`

**Depends on**: T1

**Done when**:

- [ ] O arquivo usa `apiVersion: 1` e provider do tipo `file`
- [ ] O provider usa `name: OTel PoC`, `orgId: 1` e `folder: OTel PoC`
- [ ] `allowUiUpdates: false` e `disableDeletion: false` estao configurados conforme o design
- [ ] `options.path` aponta para `/otel-lgtm/dashboards`
- [ ] `foldersFromFilesStructure: false` e `updateIntervalSeconds` ficam alinhados ao design aprovado
- [ ] O provider novo nao sobrescreve nem edita `grafana-dashboards.yaml` da imagem

**Verification**:

- Local: o YAML e legivel e coerente com o design aprovado
- Runtime: nao aplicavel nesta tarefa

---

### T3: Montar os artefatos Grafana no servico `lgtm`

**What**: Ajustar o `docker-compose.yaml` para montar apenas os volumes read-only
necessarios ao provider e ao dashboard da PoC no container `lgtm`, sem alterar
collector, servicos .NET, Kafka, Postgres ou contracts da baseline.

**Where**: `docker-compose.yaml`

**Depends on**: T1, T2

**Done when**:

- [ ] O servico `lgtm` recebe mount read-only do provider para `/otel-lgtm/grafana/conf/provisioning/dashboards/otel-poc-dashboards.yaml`
- [ ] O servico `lgtm` recebe mount read-only do JSON versionado para `/otel-lgtm/dashboards/otel-poc-overview.json`
- [ ] Nenhum volume monta arquivos da PoC em caminhos padrao incorretos como `/etc/grafana/...`
- [ ] Nenhum outro servico do compose sofre mudanca funcional nesta feature
- [ ] `docker compose config` continua valido apos a alteracao

**Verification**:

- Local: `docker compose config` passa sem erro
- Runtime: nao aplicavel diretamente nesta tarefa

---

### T4: Criar o shell do dashboard JSON versionado

**What**: Introduzir o JSON base do dashboard com metadata estavel, rows por
servico, refresh/range iniciais e binding explicito ao datasource existente.

**Where**: `grafana/dashboards/otel-poc-overview.json`

**Depends on**: T3

**Done when**:

- [ ] O dashboard possui `uid` estavel, titulo estavel e tags leves da PoC
- [ ] O JSON fica organizado para 3 rows ou secoes equivalentes: `OrderService`, `ProcessingWorker` e `NotificationWorker`
- [ ] O dashboard define refresh automatico moderado e time range inicial coerente com a demo
- [ ] Os targets usam objeto de datasource com `type: prometheus` e `uid: prometheus`, sem depender apenas do nome textual
- [ ] O arquivo permanece versionado e imutavel por provisioning, sem assumir edicao manual via UI

**Verification**:

- Local: o JSON e valido e pode ser inspecionado sem placeholders ambiguos de datasource
- Runtime: nao aplicavel nesta tarefa

---

### T5: Configurar os paineis do OrderService com queries finais

**What**: Preencher a row do `OrderService` com os 3 paineis minimos de
throughput, latencia P50/P95 e backlog, usando apenas as series normalizadas ja
validadas no backend.

**Where**: `grafana/dashboards/otel-poc-overview.json`

**Depends on**: T4

**Done when**:

- [ ] Existe painel de throughput com `rate(orders_created_total{service_name="order-service"}[5m])` agregado por `result`
- [ ] Existe painel de latencia com P50 e P95 baseados em `orders_create_duration_milliseconds_bucket`
- [ ] Existe painel de backlog com `orders_backlog_current{service_name="order-service"}` agregado por `status`
- [ ] O painel de backlog expoe apenas `pending_publish` e `publish_failed` como eixo esperado de leitura
- [ ] Nenhuma query do `OrderService` usa nomes pre-normalizacao ou labels de alta cardinalidade

**Verification**:

- Local: as queries do `OrderService` no JSON coincidem com o catalogo do design
- Runtime: comparacao no Explore fica pendente para T10

---

### T6: Configurar os paineis do ProcessingWorker com queries finais

**What**: Preencher a row do `ProcessingWorker` com os 3 paineis minimos de
throughput, latencia P50/P95 e consumer lag agregado do topic `orders`.

**Where**: `grafana/dashboards/otel-poc-overview.json`

**Depends on**: T4

**Done when**:

- [ ] Existe painel de throughput com `rate(orders_processed_total{service_name="processing-worker"}[5m])` agregado por `result`
- [ ] Existe painel de latencia com P50 e P95 baseados em `orders_processing_duration_milliseconds_bucket`
- [ ] Existe painel de lag com `kafka_consumer_lag{service_name="processing-worker",topic="orders"}` agregado por `topic` e `consumer_group`
- [ ] O painel de lag permanece agregado e nao abre recorte por particao nesta iteracao
- [ ] Nenhuma query do `ProcessingWorker` usa nomes antigos, media simplificada por `_sum/_count` ou labels fora do escopo aprovado

**Verification**:

- Local: as queries do `ProcessingWorker` no JSON coincidem com o catalogo do design
- Runtime: comparacao no Explore fica pendente para T10

---

### T7: Configurar os paineis do NotificationWorker com queries finais

**What**: Preencher a row do `NotificationWorker` com os 3 paineis minimos de
throughput, latencia P50/P95 e consumer lag agregado do topic `notifications`.

**Where**: `grafana/dashboards/otel-poc-overview.json`

**Depends on**: T4

**Done when**:

- [ ] Existe painel de throughput com `rate(notifications_persisted_total{service_name="notification-worker"}[5m])` agregado por `result`
- [ ] Existe painel de latencia com P50 e P95 baseados em `notifications_persistence_duration_milliseconds_bucket`
- [ ] Existe painel de lag com `kafka_consumer_lag{service_name="notification-worker",topic="notifications"}` agregado por `topic` e `consumer_group`
- [ ] O painel continua orientado ao hop final da pipeline, sem reabrir escopo de alertas ou novos sinais
- [ ] Nenhuma query do `NotificationWorker` usa nomes pre-normalizacao ou dimensoes fora de `result`, `topic` e `consumer_group`

**Verification**:

- Local: as queries do `NotificationWorker` no JSON coincidem com o catalogo do design
- Runtime: comparacao no Explore fica pendente para T10

---

### T8: Validar binding do datasource e os caminhos reais de provisioning

**What**: Verificar explicitamente que o compose, o provider e o dashboard
versionado estao alinhados ao runtime real da imagem `grafana/otel-lgtm` e ao
datasource Prometheus ja existente.

**Where**: `docker-compose.yaml`, `grafana/provisioning/dashboards/otel-poc-dashboards.yaml`, `grafana/dashboards/otel-poc-overview.json`, runtime do container `lgtm`

**Depends on**: T2, T3, T5, T6, T7

**Done when**:

- [ ] Os mounts efetivos aparecem em `/otel-lgtm/grafana/conf/provisioning/dashboards` e `/otel-lgtm/dashboards`
- [ ] O provider da PoC esta presente sem sobrescrever os arquivos nativos do LGTM
- [ ] O datasource Prometheus existente continua vindo da baseline da imagem, sem arquivo novo criado pela feature
- [ ] O JSON final preserva `uid: prometheus` em todos os targets relevantes
- [ ] Nao existe referencia residual a datasource local temporario, datasource por nome apenas ou caminhos errados de provisioning

**Verification**:

- Local: inspecao dos arquivos confirma os paths e o binding esperados
- Runtime: `docker compose exec -T lgtm` confirma a presenca dos arquivos montados e do datasource `uid: prometheus`

---

### T9: Validar o auto-provisionamento funcional do dashboard no Grafana

**What**: Recarregar o servico `lgtm` e confirmar que a pasta `OTel PoC` e o
dashboard versionado aparecem automaticamente no Grafana, sem criacao manual no
UI e sem erro de datasource nos paineis.

**Where**: ambiente Docker Compose e UI/API do Grafana do stack atual

**Depends on**: T8

**Done when**:

- [ ] O restart ou `docker compose up -d` do `lgtm` recarrega o provider da PoC sem erro novo relevante
- [ ] A pasta `OTel PoC` aparece automaticamente no Grafana
- [ ] O dashboard `otel-poc-overview` aparece automaticamente sem import manual
- [ ] Os paineis carregam sem erro de datasource nao encontrado
- [ ] O provisionamento continua reproduzivel apos nova subida do ambiente

**Verification**:

- Local: logs do `lgtm` nao mostram falha de provisioning relacionada a feature
- Grafana: a UI confirma a presenca automatica da pasta e do dashboard

---

### T10: Validar queries PromQL normalizadas e ausencia de regressao de escopo

**What**: Confrontar o dashboard com o Explore/Prometheus para garantir que as
queries finais usam exatamente os nomes normalizados com
`*_duration_milliseconds_*` e que a implementacao nao expandiu o escopo da
feature para alertas, metricas novas, collector ou servicos .NET.

**Where**: Grafana dashboard, Explore/Prometheus e diff local da feature

**Depends on**: T9

**Done when**:

- [ ] Pelo menos uma query de cada servico e comparada entre dashboard e Explore usando as mesmas series base
- [ ] Todas as queries de percentil usam `orders_create_duration_milliseconds_bucket`, `orders_processing_duration_milliseconds_bucket` e `notifications_persistence_duration_milliseconds_bucket`
- [ ] Throughput e lag usam apenas `orders_created_total`, `orders_processed_total`, `notifications_persisted_total`, `orders_backlog_current` e `kafka_consumer_lag`
- [ ] Nenhuma mudanca foi feita em servicos .NET, `otelcol.yaml`, processors, spans, logs, contratos Kafka, payloads ou persistencia
- [ ] Nenhum artefato de alerta, contact point, regra Grafana, datasource novo ou metrica nova foi introduzido nesta iteracao

**Verification**:

- Local: diff final da feature fica restrito a `docker-compose.yaml` e artefatos Grafana versionados
- Grafana/Prometheus: Explore confirma a forma final normalizada das queries

---

## Validation Notes

- A imagem `grafana/otel-lgtm:latest` usada na baseline atual expoe o Grafana em `/otel-lgtm/grafana`, nao em `/etc/grafana`
- O diretorio real de provisioning validado no runtime e `/otel-lgtm/grafana/conf/provisioning`
- O datasource Prometheus existente da imagem usa `uid: prometheus` e deve ser apenas reutilizado
- Esta feature continua restrita a visualizacao versionada; qualquer necessidade de alerta ou novo sinal deve ser empurrada para a feature posterior `Alertas Grafana`

---

## Parallel Execution Map

```text
Phase 1:
  T1 -> T2 -> T3

Phase 2:
  T3 -> T4
  T4 -> T5
  T4 -> T6
  T4 -> T7

Phase 3:
  T2 + T3 + T5 + T6 + T7 -> T8 -> T9 -> T10
```
