# Alertas Grafana - Design

**Spec**: `.specs/features/alertas-grafana/spec.md`
**Status**: Designed

---

## Architecture Overview

Esta feature continua inteiramente no plano de configuracao do Grafana. A cadeia
de telemetria validada em M2/M3 permanece a mesma:

1. os servicos .NET continuam emitindo metricas para `otelcol:4317`;
2. o `otelcol` continua exportando metricas para o LGTM em
   `http://lgtm:4318/v1/metrics`;
3. o Prometheus embarcado no `grafana/otel-lgtm:latest` continua sendo a fonte
   de dados das regras, via datasource provisionado `uid: prometheus`;
4. o Grafana embutido passa a carregar tambem artefatos de unified alerting por
   file provisioning.

Validacao de runtime realizada no ambiente atual:

1. a imagem em uso e Grafana `12.4.1`;
2. a raiz real de provisioning continua sendo
   `/otel-lgtm/grafana/conf/provisioning`;
3. o diretorio real de alerting existe em
   `/otel-lgtm/grafana/conf/provisioning/alerting` e a imagem ja traz
   `/otel-lgtm/grafana/conf/provisioning/alerting/sample.yaml` como referencia
   de schema;
4. o datasource Prometheus ativo continua provisionado em
   `/otel-lgtm/grafana/conf/provisioning/datasources/grafana-datasources.yaml`
   com `uid: prometheus`;
5. o dashboard versionado da PoC segue montado em `/otel-lgtm/dashboards` e usa
   `uid: otel-poc-m3-overview`, com os paineis obrigatorios ja existentes para
   associacao das regras.

Arquitetura alvo da implementacao:

1. adicionar artefatos versionados de alerting no repositorio, separados dos
   dashboards;
2. montar esses artefatos no servico `lgtm` por volumes read-only apontando para
   `/otel-lgtm/grafana/conf/provisioning/alerting`;
3. manter o dashboard como baseline visual e ligar cada alerta ao dashboard/painel
   correspondente quando isso reduzir ambiguidade operacional;
4. provisionar um contact point local e uma policy minima, sem canais externos;
5. validar `firing` e `resolved` com um receiver local simples, sem tocar
   `otelcol.yaml`, processors ou servicos .NET.

---

## Design Decisions

### Provisionar alerting no diretorio nativo da imagem, sem imagem derivada

**Decision**: A feature deve montar arquivos YAML diretamente em
`/otel-lgtm/grafana/conf/provisioning/alerting`, sem criar uma imagem custom do
LGTM.

**Reason**: O runtime atual ja confirmou que a imagem `grafana/otel-lgtm:latest`
carrega unified alerting por esse diretorio. Reaproveitar esse comportamento
mantem o diff pequeno e consistente com a baseline de `dashboard-grafana`.

**Trade-off**: O design continua acoplado aos caminhos internos da imagem atual,
entao a validacao de runtime permanece obrigatoria na implementacao.

### Separar regras, contact points e policy em artefatos distintos

**Decision**: A implementacao deve versionar tres artefatos de alerting
separados: um para grupos de regras, um para contact points e um para a policy
tree minima.

**Reason**: Essa separacao reduz ruido de diff e deixa claro qual parte do
alerting foi alterada em cada iteracao, sem misturar a definicao das regras com
o roteamento.

**Trade-off**: O compose passa a montar tres arquivos em vez de um. Em troca,
fica mais facil revisar e manter a feature.

### A policy tree deve ter dono unico e minimo

**Decision**: Apenas um arquivo versionado da PoC deve definir `policies`, com
uma raiz minima e sem sub-rotas na primeira iteracao.

**Reason**: O Grafana trata a notification policy tree como recurso unico; o
provisionamento sobrescreve a arvore inteira. Concentrar a policy minima em um
arquivo unico reduz o risco de sobrescrita acidental ou drift futuro.

**Trade-off**: O primeiro corte abre mao de roteamento mais rico por severidade
ou time. Isso e aceitavel porque a PoC tem apenas dois alertas obrigatorios.

### Receiver local preferencial via webhook mock em container dedicado

**Decision**: O caminho preferencial para notificacao local deve ser um
`webhook` apontando para um helper local simples na mesma rede Docker, com log
inspecionavel por `docker compose logs`.

**Reason**: Essa abordagem demonstra payload, timestamps, `firing` e `resolved`
de forma verificavel, sem depender de secrets, email, Slack ou qualquer servico
externo.

**Trade-off**: A implementacao inclui um helper de ambiente adicional. Isso e
aceitavel porque o helper permanece fora da telemetria da aplicacao e nao toca
metricas, labels ou spans.

### Regras devem apontar para o dashboard ja provisionado quando houver painel correspondente

**Decision**: As duas regras obrigatorias devem carregar `dashboardUid` e
`panelId` apontando para o dashboard versionado existente da PoC.

**Reason**: O dashboard ja tem `uid: otel-poc-m3-overview` e os paineis
relevantes ja existem. Associar regra e painel melhora a navegacao operacional e
mantem coerencia entre visualizacao e alerta.

**Trade-off**: A feature de alertas passa a depender de ids estaveis de painel.
Como esses ids ja estao versionados no JSON, a dependencia e aceitavel.

### A forma final dos modelos de query deve nascer de export do proprio Grafana

**Decision**: Na implementacao, o esqueleto final de `data.model` para regras
Prometheus + reduce/threshold deve ser obtido por export do Grafana 12.4.1
depois de criar uma regra semente equivalente, e entao limpo/versionado no
repositorio.

**Reason**: O schema de provisioning exposto pelo `sample.yaml` cobre a estrutura
geral, mas o modelo concreto de queries Prometheus e expressoes `__expr__` e
verboso e sensivel a versao. Exportar uma regra real reduz risco de YAML valido
mas semanticamente quebrado.

**Trade-off**: A implementacao inclui um passo explicito de captura/export antes
da consolidacao final dos artefatos versionados.

---

## Proposed File Layout

Estrutura esperada no repositorio:

```text
grafana/
  dashboards/
    otel-poc-overview.json
  provisioning/
    dashboards/
      otel-poc-dashboards.yaml
    alerting/
      otel-poc-alert-rules.yaml
      otel-poc-contact-points.yaml
      otel-poc-notification-policies.yaml
```

Mapeamento esperado no container `lgtm`:

```text
./grafana/provisioning/alerting/otel-poc-alert-rules.yaml
  -> /otel-lgtm/grafana/conf/provisioning/alerting/otel-poc-alert-rules.yaml

./grafana/provisioning/alerting/otel-poc-contact-points.yaml
  -> /otel-lgtm/grafana/conf/provisioning/alerting/otel-poc-contact-points.yaml

./grafana/provisioning/alerting/otel-poc-notification-policies.yaml
  -> /otel-lgtm/grafana/conf/provisioning/alerting/otel-poc-notification-policies.yaml
```

Detalhe de ambiente previsto para o receiver local:

```text
docker-compose.yaml
  + helper local opcional `alert-webhook-mock`
```

O helper local nao faz parte dos artefatos de telemetria da aplicacao. Ele e um
apoio de ambiente para validar notificacoes Grafana.

---

## Provisioning Strategy

### Surface area esperada no compose

Mudancas funcionais previstas:

1. adicionar tres mounts read-only ao servico `lgtm`, todos sob
   `/otel-lgtm/grafana/conf/provisioning/alerting`;
2. adicionar um helper local de receiver apenas se necessario para validar o
   webhook mock de forma reproduzivel;
3. nao tocar `otelcol`, `order-service`, `processing-worker` ou
   `notification-worker`.

### Estrategia de carga

1. o Grafana carrega automaticamente arquivos de alerting presentes em
   `provisioning/alerting` no startup;
2. a validacao da feature deve usar restart do `lgtm` como caminho baseline de
   recarga, por ser o fluxo mais simples e reproduzivel da PoC;
3. hot reload por Admin API pode ser tratado como opcional e nao e pre-requisito
   para concluir a feature;
4. a verificacao de sucesso precisa checar a presenca fisica dos arquivos
   montados no container e a aparicao das regras/contact point/policy no Grafana.

### Ownership dos recursos

1. `otel-poc-alert-rules.yaml` e o unico dono das duas regras obrigatorias da PoC;
2. `otel-poc-contact-points.yaml` e o unico dono do contact point local da PoC;
3. `otel-poc-notification-policies.yaml` e o unico dono da policy tree minima da
   PoC;
4. edicoes manuais no UI nao devem virar fonte de verdade; qualquer ajuste deve
   voltar para os arquivos versionados.

---

## Alert Rule Design

## Grupo de regras

- `orgId: 1`
- `name: otel-poc-m3-alerts`
- `folder: OTel PoC`
- `interval: 30s`

Esse grupo concentra apenas os dois alertas obrigatorios do milestone. O uso de
`30s` reduz o tempo ate o estado `Pending/Firing` sem deixar a demo lenta para
validacao manual.

### Regra 1: OrderService P95 acima de 500 ms

- `uid`: `otel_poc_order_p95_high`
- `title`: `OrderService P95 > 500 ms`
- `dashboardUid`: `otel-poc-m3-overview`
- `panelId`: `2`
- `for`: `1m`
- `noDataState`: `OK`
- `execErrState`: `Error`

Query base aprovada:

```promql
histogram_quantile(
  0.95,
  sum by (le) (
    rate(orders_create_duration_milliseconds_bucket{service_name="order-service",result="created"}[5m])
  )
)
```

Pipeline logico esperado da regra:

1. consulta Prometheus retorna o P95 agregado do `OrderService`;
2. etapa de reduce usa `last` sobre a serie avaliada;
3. etapa de threshold verifica `> 500`;
4. a condicao final aponta para a expressao de threshold.

Labels minimas sugeridas:

- `severity: warning`
- `service: order-service`
- `signal: latency`
- `scope: otel-poc`

Annotations minimas sugeridas:

- `summary: Latencia P95 de criacao acima de 500 ms por 1 minuto`
- `description: O caminho feliz de POST /orders permaneceu acima do alvo de latencia na janela avaliada`
- `runbook: local-demo`

### Regra 2: ProcessingWorker lag acima de 100 mensagens

- `uid`: `otel_poc_processing_lag_high`
- `title`: `ProcessingWorker lag > 100`
- `dashboardUid`: `otel-poc-m3-overview`
- `panelId`: `6`
- `for`: `1m`
- `noDataState`: `OK`
- `execErrState`: `Error`

Query base aprovada:

```promql
sum by (topic, consumer_group) (
  kafka_consumer_lag{service_name="processing-worker",topic="orders"}
)
```

Pipeline logico esperado da regra:

1. consulta Prometheus retorna o lag agregado do consumidor principal;
2. etapa de reduce usa `last`;
3. etapa de threshold verifica `> 100`;
4. a condicao final aponta para a expressao de threshold.

Labels minimas sugeridas:

- `severity: warning`
- `service: processing-worker`
- `signal: consumer_lag`
- `scope: otel-poc`

Annotations minimas sugeridas:

- `summary: ProcessingWorker acumulou mais de 100 mensagens de lag por 1 minuto`
- `description: O consumidor do topic orders permaneceu atrasado acima do threshold acordado`
- `runbook: local-demo`

### Decisoes operacionais das regras

1. `noDataState: OK` evita falso positivo quando a PoC estiver ociosa e sem carga;
2. `execErrState: Error` deixa falhas de avaliacao visiveis como problema de
   configuracao ou datasource, sem mascarar essas falhas como alerta de negocio;
3. as duas regras devem continuar usando as queries normalizadas ja aprovadas no
   dashboard, sem reinterpretar nomes ou inventar metricas derivadas novas.

---

## Contact Point And Policy Design

### Contact point local minimo

Contato proposto:

- `name`: `OTel PoC Local Webhook`
- `orgId`: `1`
- `receiver uid`: `otel_poc_local_webhook`
- `type`: `webhook`
- `disableResolveMessage`: `false`

Settings minimos esperados:

- `url: http://alert-webhook-mock:8080/`
- `httpMethod: POST` se o export do Grafana materializar esse campo
- cabecalhos extras opcionais apenas se o helper escolhido exigir algo

Diretrizes:

1. nao usar auth, TLS custom, HMAC ou payload custom na primeira iteracao;
2. manter o payload padrao do Grafana porque ele ja traz `status`, `alerts`,
   `commonLabels`, `dashboardURL` e `panelURL` quando disponiveis;
3. manter mensagens de `resolved` habilitadas para demonstrar o ciclo completo.

### Policy tree minima

Policy root proposta:

- `orgId: 1`
- `receiver: OTel PoC Local Webhook`
- `group_by: [alertname, grafana_folder]`
- `group_wait: 0s`
- `group_interval: 1m`
- `repeat_interval: 4h`
- sem `routes` filhas na primeira iteracao

Racional:

1. `group_wait: 0s` acelera a demonstracao do primeiro disparo local;
2. `group_by: [alertname, grafana_folder]` preserva agrupamento minimo sem misturar
   alertas de nomes diferentes no mesmo evento;
3. nao criar sub-rotas reduz risco de erro, ja que ambos os alertas obrigatorios
   apontam para o mesmo receiver local.

---

## Local Receiver Strategy

O design assume um helper local simples com estas propriedades:

1. HTTP acessivel pelo hostname `alert-webhook-mock` na rede `otel-demo`;
2. request body e headers visiveis via logs do container;
3. sem persistencia obrigatoria;
4. sem dependencia de credenciais ou internet;
5. comportamento estavel o bastante para demonstrar `firing` e `resolved`.

Opcao preferencial para implementacao:

1. servico Docker dedicado baseado em imagem de echo/mock HTTP pronta, por
   exemplo `mendhak/http-https-echo` ou equivalente leve;
2. validacao por `docker compose logs alert-webhook-mock`.

Fallback aceitavel:

1. receiver local baseado em log simples, desde que o payload do Grafana permaneca
   observavel e versionavel no fluxo de validacao.

---

## Validation Strategy

Checklist minimo esperado para a implementacao:

1. `docker compose config` passa com os novos mounts e, se adotado, com o helper
   local de webhook;
2. apos restart do `lgtm`, os arquivos versionados aparecem em
   `/otel-lgtm/grafana/conf/provisioning/alerting`;
3. o datasource Prometheus continua visivel com `uid: prometheus` e sem drift no
   provisioning atual;
4. a UI do Grafana mostra as duas regras provisionadas em `Alerts & IRM`, dentro
   da pasta `OTel PoC`;
5. o contact point local provisionado aparece na UI e a policy tree minima aponta
   para ele;
6. o alerta de latencia consegue entrar em `Pending` e `Firing` com degradacao
   controlada do `POST /orders`, sem alterar contratos ou instrumentacao;
7. o alerta de lag consegue entrar em `Pending` e `Firing` com acumulacao
   controlada de mensagens no `ProcessingWorker`, sem novas metricas;
8. o receiver local registra payloads de `firing` e `resolved` contendo pelo
   menos `status`, `alerts`, labels e referencias de dashboard/painel quando
   disponiveis;
9. o diff final da implementacao permanece restrito a artefatos Grafana,
   `docker-compose.yaml` e, se necessario, ao helper local de receiver.

### Validacoes de consulta obrigatorias

1. confrontar a query de latencia da regra com a mesma expressao em Explore;
2. confrontar a query de lag da regra com a mesma expressao em Explore;
3. garantir que as expressoes finais continuem usando apenas
   `orders_create_duration_milliseconds_bucket` e `kafka_consumer_lag`.

---

## Implementation Boundaries

Permanece explicitamente fora de escopo nesta feature:

1. qualquer alteracao em metricas, labels, spans, logs, collector ou pipelines
   OTLP;
2. qualquer mudanca em contratos Kafka, payloads, topicos, persistencia ou
   servicos .NET;
3. qualquer integracao real com email, Slack, Teams, PagerDuty ou similares;
4. qualquer alerta adicional alem dos dois minimos exigidos;
5. qualquer reestruturacao do dashboard que nao seja estritamente necessaria para
   manter o link dashboard/painel das regras.