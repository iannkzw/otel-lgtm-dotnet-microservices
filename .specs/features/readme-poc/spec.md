# README da PoC - Specification

**Milestone**: M4 - Hardening e Documentacao da PoC
**Status**: Specified

---

## Problem Statement

O roadmap de M4 ja posiciona a `README da PoC` como o primeiro passo para
fechar a demonstracao do ambiente, mas o `README.md` atual ainda reflete uma
baseline anterior do repositorio. Hoje ele descreve apenas a stack base de
collector/LGTM e ainda afirma que os tres servicos .NET nao entram no compose,
o que ja nao e verdade desde M1/M2/M3. Tambem faltam instrucoes consolidadas
para subir o ambiente completo, gerar o fluxo feliz de pedidos, localizar os
enderecos relevantes no host e inspecionar traces, metricas, logs, dashboard e
alertas sem depender do conhecimento tacito acumulado na conversa.

Esta feature deve especificar um README unico, pragmatico e reproduzivel para a
PoC, consolidando a baseline ja validada de `order-service`,
`processing-worker`, `notification-worker`, `otelcol`, `lgtm`, `kafka`,
`postgres` e `alert-webhook-mock`. O escopo e estritamente documental: a
feature nao deve reabrir codigo de aplicacao, collector, metricas, dashboards,
alertas, contratos Kafka ou configuracoes funcionais que ja foram estabilizadas
nos milestones anteriores.

## Goals

- [ ] Especificar a feature `readme-poc` como primeiro passo do milestone M4,
      consolidando a execucao e a demonstracao da stack entregue em M1, M2 e M3
- [ ] Definir claramente o publico-alvo do README para tres cenarios: primeira
      execucao local, walkthrough da demo e referencia rapida de observabilidade
- [ ] Determinar as secoes minimas obrigatorias do README: visao geral da
      arquitetura, pre-requisitos, subida do ambiente, fluxo feliz, inspecao de
      traces/metricas/logs/alertas e troubleshooting basico
- [ ] Reutilizar apenas dependencias, evidencias e artefatos ja validados no
      estado atual da PoC, sem criar passos que dependam de features ainda nao
      implementadas
- [ ] Tornar explicita a distincao entre URLs acessiveis no host e endpoints
      internos da rede Docker, evitando instrucoes enganosas na demonstracao
- [ ] Produzir criterios de aceite que separem claramente documentacao util e
      reproduzivel de qualquer mudanca funcional no sistema

## Out of Scope

- Refatoracoes de codigo, ajustes de comportamento, correcoes funcionais ou
  qualquer mudanca nos servicos `OrderService`, `ProcessingWorker` e
  `NotificationWorker`
- Novas features de observabilidade, novas metricas, novos dashboards, novas
  regras de alerta ou novos receivers de notificacao
- Alteracoes em `otelcol.yaml`, processors em `processors/`, exporters, tail
  sampling, pipelines OTLP ou configuracoes base do collector/LGTM
- Mudancas em contratos HTTP, contratos Kafka, payloads, topicos, schemas,
  bootstrap de banco, persistencia, retry, DLQ, outbox ou politicas de erro
- Exposicao de novas portas no compose apenas para acomodar o README
- Reescrever a historia da PoC ou transformar o README em documentacao exaustiva
  de implementacao interna; o objetivo e reproducao local e demonstracao

---

## Current Baseline To Reuse

### Servicos e artefatos ja existentes

- `order-service` exposto no host em `http://localhost:8080`
- `lgtm` exposto no host em `http://localhost:3000` com `admin/admin`
- `otelcol` exposto no host em `localhost:4317` (OTLP gRPC) e `localhost:4318`
  (OTLP HTTP)
- `processing-worker`, `notification-worker`, `kafka`, `postgres` e
  `zookeeper` operando apenas na rede Docker da PoC
- `alert-webhook-mock` rodando internamente na rede Docker em
  `http://alert-webhook-mock:8080`, sem porta publicada no host no compose atual
- Dashboard versionado em `grafana/dashboards/otel-poc-overview.json`
- Alertas Grafana provisionados em `grafana/provisioning/alerting/`
- Webhook mock local em `tools/alert-webhook-mock/server.py`, com endpoints
  internos `/health` e `/requests`

### Fluxos ja validados que o README deve reaproveitar

- Subida completa do ambiente com `docker compose up -d --build`
- Fluxo feliz de `POST /orders` gerando persistencia, publish Kafka,
  processamento no `ProcessingWorker`, publish em `notifications` e persistencia
  final no `NotificationWorker`
- Visualizacao de traces no Tempo, metricas no Prometheus/Grafana, logs no Loki
  e dashboard provisionado no Grafana
- Validacao de alertas Grafana com entrega local no `alert-webhook-mock`

### Lacunas atuais de documentacao

- O `README.md` atual ainda descreve uma fase anterior do projeto e nao reflete
  a baseline de M1/M2/M3
- Nao ha um passo a passo unico para subir todos os containers e validar os
  sinais da PoC em ambiente limpo
- Nao ha referencia unica de URLs locais, credenciais, comandos de verificacao
  e localizacao dos artefatos de demo
- O receiver de alertas existe, mas hoje sua acessibilidade e interna ao compose;
  isso precisa ficar explicito na documentacao para evitar tentativa de uso de
  uma URL `localhost` inexistente

---

## Audiences And Usage Scenarios

## Publico-alvo principal

- Pessoa desenvolvedora ou avaliadora que precisa rodar a PoC localmente pela
  primeira vez sem depender do historico da conversa
- Pessoa apresentadora da demo que precisa de um roteiro curto para disparar o
  fluxo feliz e navegar pelos sinais de observabilidade
- Pessoa que ja conhece a stack e precisa de uma referencia rapida de URLs,
  comandos e onde inspecionar traces, metricas, logs e alertas

## Cenarios obrigatorios de uso

### Cenario 1: Primeira execucao local

O README deve permitir que um ambiente limpo consiga:

1. entender rapidamente quais componentes sobem no compose;
2. conferir pre-requisitos reais do host;
3. iniciar o ambiente com um comando principal;
4. verificar se os containers essenciais estao saudaveis.

### Cenario 2: Walkthrough da demo

O README deve permitir que a pessoa usuaria:

1. gere um pedido de exemplo no `order-service`;
2. acompanhe os sinais da jornada nos tres servicos;
3. abra o dashboard versionado e a area de Explore do Grafana;
4. valide que os alertas e o receiver local ja fazem parte da baseline.

### Cenario 3: Referencia rapida de observabilidade

O README deve servir como consulta rapida para:

1. URLs e credenciais locais;
2. servicos internos vs servicos expostos no host;
3. comandos de verificacao basicos;
4. localizacao de traces, metricas, logs, dashboard e evidencias de alerta.

---

## Required README Structure

## Secao 1: Visao geral da arquitetura

O README deve abrir com uma explicacao curta da PoC e do fluxo principal:

- entrada HTTP em `order-service`;
- propagacao por Kafka entre `orders` e `notifications`;
- persistencia em PostgreSQL;
- exportacao OTLP via `otelcol`;
- consumo dos sinais no stack LGTM;
- dashboard e alertas como camada de demonstracao operacional.

Esta secao deve mencionar explicitamente os servicos obrigatorios do ambiente:
`order-service`, `processing-worker`, `notification-worker`, `otelcol`, `lgtm`,
`kafka`, `postgres` e `alert-webhook-mock`.

## Secao 2: Pre-requisitos reais

O README deve listar apenas pre-requisitos comprovados no estado atual:

- Docker Desktop ou ambiente Docker equivalente com suporte a `docker compose`;
- portas do host necessarias para a demo (`3000`, `8080`, `4317`, `4318`);
- acesso a shell local para executar comandos HTTP e `docker compose`.

Tambem deve ficar explicito que:

- a subida e a demonstracao da PoC nao dependem de .NET 10 SDK instalado no
  host, pois o caminho primario da demo e Docker Compose;
- builds .NET locais fora do Docker sao opcionais e hoje permanecem condicionais
  ao ambiente do host.

## Secao 3: Como subir o ambiente

O README deve conter um fluxo de bootstrap minimo e reproduzivel:

1. comando principal para subir o ambiente completo com build;
2. comando simples para verificar containers/health;
3. resultado esperado em alto nivel, com destaque para os servicos criticos;
4. observacao clara sobre o tempo inicial de subida e provisionamento do Grafana.

Esta secao deve evitar scripts novos e depender apenas do compose atual.

## Secao 4: Como gerar o fluxo feliz

O README deve definir um roteiro minimo para produzir o caminho feliz da PoC:

1. enviar `POST /orders` para `http://localhost:8080/orders`;
2. capturar o `orderId` retornado;
3. opcionalmente consultar `GET /orders/{id}` para confirmar o estado no
   `order-service`;
4. deixar claro que o restante do fluxo segue por Kafka e workers, sem chamadas
   manuais adicionais obrigatorias.

O foco da documentacao deve ser reproducao da demo, nao exploracao exaustiva de
  casos de erro.

## Secao 5: Como inspecionar traces

O README deve orientar a abrir o Grafana e localizar traces no Tempo com base no
fluxo `POST /orders`, incluindo expectativa minima de servicos/span hops
observaveis no trace distribuido.

## Secao 6: Como inspecionar metricas e dashboard

O README deve orientar:

1. onde abrir o dashboard versionado da PoC no Grafana;
2. como localizar as metricas principais no Explore/Prometheus;
3. quais sinais observar por servico em alto nivel: throughput, latencia,
   backlog e consumer lag.

## Secao 7: Como inspecionar logs

O README deve indicar o caminho minimo para localizar logs estruturados no Loki
ou, quando necessario, nos logs de container, com foco em correlacao pratica da
demo e nao em taxonomia interna completa.

## Secao 8: Como inspecionar alertas

O README deve explicar:

1. onde ver as regras provisionadas no Grafana;
2. que o receiver local `alert-webhook-mock` faz parte da baseline atual;
3. que o mock nao esta exposto em `localhost` no compose atual;
4. que a verificacao do recebimento pode usar `docker compose logs
   alert-webhook-mock` e, quando apropriado, inspecao interna do endpoint
   `/requests` no proprio container/rede Docker.

## Secao 9: Troubleshooting basico

O README deve cobrir pelo menos:

- conflito de portas do host;
- containers nao saudaveis ou ainda inicializando;
- dashboard/alerta nao aparecendo imediatamente por atraso de provisioning;
- ambiente Windows sem .NET 10 SDK local, deixando claro que isso nao bloqueia a
  execucao principal da PoC via Docker.

---

## Documentation Principles

### Pragmatismo

- Priorizar comandos curtos e verificaveis sobre explicacoes longas
- Descrever apenas o que ja foi validado no ambiente atual
- Separar claramente o caminho minimo da demo de comandos opcionais de diagnostico

### Sem conhecimento tacito

- O README nao pode assumir contexto da conversa nem passos implícitos
- Toda URL ou credencial citada precisa corresponder ao estado real do compose
- Endpoints internos da rede Docker devem ser identificados como internos, nao
  como URLs locais do host

### Sem reabrir escopo funcional

- Se um passo de documentacao depender de alterar compose, codigo ou alertas, o
  passo esta fora de escopo desta feature
- O README deve documentar a baseline atual, nao induzir novas mudancas nela

---

## Acceptance Criteria

- Existe uma especificacao versionada da feature em
  `.specs/features/readme-poc/spec.md`, conectada explicitamente ao milestone M4
- A especificacao deixa explicito que a feature e documental e nao pode exigir
  mudancas funcionais em servicos, collector, metricas, dashboards, alertas,
  processors ou contratos Kafka
- O publico-alvo e os tres cenarios obrigatorios de uso estao descritos de forma
  nao ambigua: primeira execucao local, walkthrough da demo e referencia rapida
  de observabilidade
- As secoes minimas obrigatorias do README estao definidas: arquitetura,
  pre-requisitos, subida do ambiente, fluxo feliz, traces, metricas/dashboard,
  logs, alertas e troubleshooting basico
- A baseline a reutilizar menciona explicitamente os servicos `order-service`,
  `processing-worker`, `notification-worker`, `otelcol`, `lgtm`, `kafka`,
  `postgres` e `alert-webhook-mock`
- A especificacao exige diferenciar URLs do host de endpoints internos da rede
  Docker, incluindo o caso do `alert-webhook-mock`
- A especificacao deixa claro que o README deve usar como fonte de verdade os
  artefatos e validacoes ja consolidados em M1, M2 e M3

---

## Validation Intent For Future Steps

Esta especificacao considera suficiente, para as proximas etapas de design e
implementacao, que a validacao futura consiga:

1. atualizar o `README.md` sem alterar o comportamento funcional do sistema;
2. permitir que uma pessoa usuaria suba a PoC com `docker compose up -d --build`
   e encontre as URLs, credenciais e verificacoes essenciais sem ajuda externa;
3. reproduzir o fluxo feliz com `POST /orders` e localizar os sinais no Grafana;
4. entender como verificar alertas e o webhook mock usando os caminhos reais do
   ambiente atual, sem inventar exposicoes `localhost` inexistentes.