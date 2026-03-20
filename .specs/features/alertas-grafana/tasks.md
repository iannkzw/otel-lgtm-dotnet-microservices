# Alertas Grafana - Tasks

**Design**: `.specs/features/alertas-grafana/design.md`
**Status**: Tasks Defined

---

## Execution Plan

### Phase 1: Artefatos versionados de unified alerting

```text
T1 (estrutura de alerting) -> T2 (regras)
T1 -> T3 (contact point)
T1 -> T4 (policy minima)
```

### Phase 2: Compose e receiver local de validacao

```text
T2 + T3 + T4 -> T5 (mounts no lgtm)
T5 -> T6 (helper local webhook mock)
```

### Phase 3: Verificacao de runtime, datasource e comportamento

```text
T5 + T6 -> T7 (paths reais + datasource preservado)
T7 -> T8 (provisioning de regras/contact point/policy)
T8 -> T9 (queries e sinais obrigatorios)
T9 -> T10 (firing/resolved + nao-regressao)
```

---

## Task Breakdown

### T1: Criar a estrutura versionada de artefatos de alerting

**What**: Introduzir no repositorio a estrutura minima para separar os artefatos
de unified alerting da PoC dos dashboards e do baseline nativo da imagem LGTM.

**Where**: `grafana/provisioning/alerting/`

**Depends on**: feature `dashboard-grafana` concluida

**Done when**:

- [ ] Existe a estrutura `grafana/provisioning/alerting/` no workspace
- [ ] Os nomes finais dos arquivos deixam claro o ownership da PoC: regras, contact points e policy
- [ ] Nenhum artefato de datasource novo e criado nesta feature
- [ ] Nenhum arquivo novo e colocado em caminhos internos da imagem fora do design aprovado
- [ ] A estrutura preparada permite montar os arquivos diretamente em `/otel-lgtm/grafana/conf/provisioning/alerting`

**Verification**:

- Local: a estrutura e os nomes dos artefatos aparecem no workspace como previsto no design
- Runtime: nao aplicavel nesta tarefa

---

### T2: Versionar o arquivo de grupos de regras com os dois alertas obrigatorios

**What**: Criar o artefato dono das regras da PoC, com um unico grupo em
`OTel PoC`, reutilizando `uid: prometheus`, `dashboardUid` e `panelId` do
dashboard ja provisionado.

**Where**: `grafana/provisioning/alerting/otel-poc-alert-rules.yaml`

**Depends on**: T1

**Done when**:

- [ ] O arquivo usa `apiVersion: 1` e define um unico grupo `otel-poc-m3-alerts`
- [ ] O grupo usa `orgId: 1`, `folder: OTel PoC` e `interval: 30s`
- [ ] Existe a regra `OrderService P95 > 500 ms` com `uid` estavel, `for: 1m`, `noDataState: OK` e `execErrState: Error`
- [ ] Existe a regra `ProcessingWorker lag > 100` com `uid` estavel, `for: 1m`, `noDataState: OK` e `execErrState: Error`
- [ ] As duas regras usam objeto de datasource com `uid: prometheus`, sem datasource novo ou binding textual ambiguo
- [ ] `dashboardUid: otel-poc-m3-overview` e os `panelId` correspondentes aparecem nas duas regras
- [ ] O modelo final de `data.model` nasce de export real do Grafana 12.4.1 ou equivalente validado contra o runtime atual, evitando YAML apenas teorico
- [ ] Nenhuma regra cria ou depende de metrica nova, label nova, collector novo ou ajuste em servicos .NET

**Verification**:

- Local: o YAML e coerente com o design e contem somente os dois alertas obrigatorios
- Grafana: a estrutura das queries Prometheus, reduce e threshold fica pronta para provisionamento real

---

### T3: Versionar o contact point local da PoC

**What**: Criar o artefato dono do contact point local, priorizando webhook mock
em rede Docker da PoC e mantendo mensagens de `resolved` habilitadas.

**Where**: `grafana/provisioning/alerting/otel-poc-contact-points.yaml`

**Depends on**: T1

**Done when**:

- [ ] O arquivo usa `apiVersion: 1` e define um unico contact point versionado da PoC
- [ ] O contact point usa `orgId: 1`, nome estavel e receiver `webhook`
- [ ] O receiver aponta para `http://alert-webhook-mock:8080/` ou equivalente local aprovado pelo design
- [ ] `disableResolveMessage: false` permanece habilitado
- [ ] O payload padrao do Grafana e preservado, sem auth, secrets, HMAC ou customizacao desnecessaria
- [ ] Nenhum canal externo real como email, Slack, Teams ou PagerDuty e introduzido

**Verification**:

- Local: o YAML do contact point permanece pequeno, legivel e totalmente local
- Runtime: a configuracao fica pronta para ser carregada pelo Grafana no startup

---

### T4: Versionar a policy tree minima com ownership unico

**What**: Criar o arquivo unico dono da notification policy tree da PoC, com raiz
minima apontando para o receiver local e sem sub-rotas na primeira iteracao.

**Where**: `grafana/provisioning/alerting/otel-poc-notification-policies.yaml`

**Depends on**: T1

**Done when**:

- [ ] O arquivo usa `apiVersion: 1` e define a policy tree minima da PoC
- [ ] A raiz usa o receiver local versionado da feature
- [ ] `group_by`, `group_wait`, `group_interval` e `repeat_interval` seguem o design aprovado
- [ ] Nao existem `routes` filhas na primeira iteracao
- [ ] O arquivo deixa claro que a policy tree e recurso unico e que este artefato e o unico dono versionado de `policies`
- [ ] Nenhum outro arquivo da feature tenta definir policy tree em paralelo, reduzindo risco de sobrescrita integral

**Verification**:

- Local: ha apenas um arquivo dono de `policies` dentro da feature
- Runtime: nao aplicavel isoladamente nesta tarefa

---

### T5: Montar os artefatos de alerting no servico `lgtm`

**What**: Ajustar o `docker-compose.yaml` para montar apenas os arquivos
versionados de alerting da PoC no diretorio real de unified alerting da imagem,
sem tocar collector, dashboards existentes, datasource nativo ou servicos .NET.

**Where**: `docker-compose.yaml`

**Depends on**: T2, T3, T4

**Done when**:

- [ ] O servico `lgtm` recebe mount read-only de `otel-poc-alert-rules.yaml` em `/otel-lgtm/grafana/conf/provisioning/alerting/otel-poc-alert-rules.yaml`
- [ ] O servico `lgtm` recebe mount read-only de `otel-poc-contact-points.yaml` em `/otel-lgtm/grafana/conf/provisioning/alerting/otel-poc-contact-points.yaml`
- [ ] O servico `lgtm` recebe mount read-only de `otel-poc-notification-policies.yaml` em `/otel-lgtm/grafana/conf/provisioning/alerting/otel-poc-notification-policies.yaml`
- [ ] Nenhum mount usa caminhos incorretos como `/etc/grafana/...` ou sobrescreve arquivos nativos de datasource
- [ ] Nenhum outro servico do compose sofre mudanca funcional nesta tarefa
- [ ] `docker compose config` continua valido apos a alteracao

**Verification**:

- Local: `docker compose config` passa sem erro
- Runtime: a verificacao efetiva dos mounts fica para T7

---

### T6: Provisionar o helper local de webhook mock para validacao de notificacoes

**What**: Adicionar o receiver local simples usado pelo contact point, com logs
inspecionaveis e sem dependencia de internet, credenciais ou persistencia.

**Where**: `docker-compose.yaml` e eventuais artefatos minimos de suporte do helper

**Depends on**: T5

**Done when**:

- [ ] Existe um servico local acessivel por `alert-webhook-mock` na rede do compose
- [ ] O helper responde em porta compativel com a URL do contact point
- [ ] O payload recebido do Grafana pode ser inspecionado por logs do container
- [ ] O helper nao exige secrets, configuracao externa ou acesso a internet
- [ ] O helper nao altera a telemetria dos servicos .NET nem reabre escopo de collector ou pipelines OTLP

**Verification**:

- Local: `docker compose config` continua passando com o helper incluido
- Runtime: logs do helper ficam prontos para demonstrar `firing` e `resolved`

---

### T7: Validar os caminhos reais de runtime e a preservacao do datasource existente

**What**: Confirmar explicitamente que os arquivos da feature chegam ao runtime
real do `grafana/otel-lgtm:latest` e que o datasource Prometheus existente
permanece intacto com `uid: prometheus`.

**Where**: `docker-compose.yaml`, `grafana/provisioning/alerting/*.yaml`, runtime do container `lgtm`

**Depends on**: T5, T6

**Done when**:

- [ ] Os tres arquivos da PoC aparecem em `/otel-lgtm/grafana/conf/provisioning/alerting`
- [ ] O diretorio real usado na verificacao e exatamente `/otel-lgtm/grafana/conf/provisioning/alerting`
- [ ] O arquivo nativo de datasource continua presente em `/otel-lgtm/grafana/conf/provisioning/datasources/grafana-datasources.yaml`
- [ ] O datasource Prometheus continua provisionado com `uid: prometheus`
- [ ] Nao existe arquivo novo de datasource, sobrescrita do datasource nativo nem referencia residual a datasource alternativo

**Verification**:

- Runtime: `docker compose exec -T lgtm` confirma os arquivos montados e o datasource existente
- Local: inspecao dos artefatos confirma binding explicito para `uid: prometheus`

---

### T8: Validar o auto-provisionamento de regras, contact point e policy minima

**What**: Reiniciar o `lgtm` e confirmar que o Grafana carrega automaticamente a
feature completa de alerting, inclusive a policy tree unica, sem drift de UI nem
necessidade de criacao manual.

**Where**: ambiente Docker Compose e UI/API do Grafana

**Depends on**: T7

**Done when**:

- [ ] O restart do `lgtm` recarrega os artefatos sem erro novo relevante de provisioning
- [ ] A UI do Grafana mostra as duas regras provisionadas na pasta `OTel PoC`
- [ ] O contact point local provisionado aparece na UI com o nome esperado
- [ ] A policy tree minima aparece apontando para o receiver local
- [ ] A verificacao confirma que a policy tree carregada veio do arquivo unico versionado da PoC
- [ ] Nao existe indicio de sobrescrita acidental por outro arquivo de `policies`

**Verification**:

- Grafana: UI e/ou API confirmam regras, contact point e policy
- Local: logs do `lgtm` nao mostram falha de schema ou conflito de provisioning da feature

---

### T9: Validar as queries finais e os dois sinais obrigatorios sem reabrir escopo

**What**: Confrontar no Grafana Explore as expressoes usadas nas regras com os
sinais ja validados na baseline de M3, preservando estritamente os nomes de
metricas e o datasource existente.

**Where**: regras provisionadas, Grafana Explore/Prometheus

**Depends on**: T8

**Done when**:

- [ ] A query de latencia da regra coincide com a expressao aprovada baseada em `orders_create_duration_milliseconds_bucket`
- [ ] A query de lag coincide com a expressao aprovada baseada em `kafka_consumer_lag{service_name="processing-worker",topic="orders"}`
- [ ] As regras finais continuam usando apenas `uid: prometheus`
- [ ] Nenhuma regra introduz metricas novas, labels novas, transformacoes fora do design ou dependencia de collector/pipeline alterado
- [ ] A validacao cobre explicitamente os dois sinais obrigatorios e nao expande para alertas extras do `NotificationWorker`

**Verification**:

- Grafana/Prometheus: Explore confirma a mesma forma final das queries das regras
- Local: diff final segue restrito a compose, artefatos Grafana e helper local quando adotado

---

### T10: Validar `firing` e `resolved` no receiver local e confirmar ausencia de regressao de escopo

**What**: Demonstrar o ciclo operacional completo dos dois alertas obrigatorios no
receiver local, incluindo notificacao minima e retorno a `resolved`, sem alterar
metricas, collector, pipelines OTLP, contratos Kafka, persistencia ou servicos .NET.

**Where**: ambiente Docker Compose, Grafana e logs do `alert-webhook-mock`

**Depends on**: T9

**Done when**:

- [ ] O alerta `OrderService P95 > 500 ms` consegue entrar em `Pending` e `Firing` com degradacao controlada, sem mudar codigo de aplicacao
- [ ] O alerta `ProcessingWorker lag > 100` consegue entrar em `Pending` e `Firing` com acumulacao controlada, sem novas metricas
- [ ] O receiver local registra payloads de `firing` e `resolved`
- [ ] Os payloads observados incluem pelo menos `status`, `alerts`, labels comuns e referencias de dashboard/painel quando disponiveis
- [ ] A policy minima e o contact point local participam da validacao de ponta a ponta
- [ ] Nenhuma mudanca foi feita em `otelcol.yaml`, processors, pipelines OTLP, spans, logs estruturados, contratos Kafka, payloads, persistencia ou servicos .NET

**Verification**:

- Runtime: logs do `alert-webhook-mock` e estado das regras na UI do Grafana confirmam `Pending`, `Firing` e `Resolved`
- Local: o diff final confirma que a feature permaneceu no plano configuracional aprovado

---

## Validation Notes

- A implementacao deve tratar `/otel-lgtm/grafana/conf/provisioning/alerting` como caminho real obrigatorio de runtime, nao como aproximacao teorica
- O datasource existente `uid: prometheus` deve ser preservado em todas as iteracoes; criar datasource novo nesta feature e regressao de escopo
- A policy tree e recurso unico do Grafana; qualquer arquivo adicional tentando definir `policies` deve ser tratado como risco de sobrescrita integral
- O helper local de webhook mock existe apenas para validacao da notificacao Grafana e nao pode contaminar a baseline de telemetria dos servicos de negocio
- A validacao dos dois sinais obrigatorios, do contact point local e da policy minima faz parte do fechamento da feature e nao pode ser adiada como detalhe opcional

---

## Parallel Execution Map

```text
Phase 1:
  T1 -> T2
  T1 -> T3
  T1 -> T4

Phase 2:
  T2 + T3 + T4 -> T5 -> T6

Phase 3:
  T5 + T6 -> T7 -> T8 -> T9 -> T10
```