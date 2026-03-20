# README da PoC - Design

**Spec**: `.specs/features/readme-poc/spec.md`
**Status**: Designed

---

## Architecture Overview

Esta feature continua estritamente no plano documental. A baseline funcional de
M1, M2 e M3 permanece exatamente como esta hoje:

1. o ambiente sobe por `docker compose up -d --build` a partir do compose unico
   da raiz;
2. o `order-service` recebe a entrada HTTP no host e dispara o fluxo pelos
   topics Kafka `orders` e `notifications`;
3. `processing-worker` e `notification-worker` operam apenas dentro da rede
   Docker da PoC;
4. o `otelcol` segue recebendo OTLP e exportando para o stack LGTM existente;
5. o Grafana segue expondo dashboard, Explore e alertas provisionados;
6. o receiver `alert-webhook-mock` continua sendo um servico interno da rede
   Docker, usado apenas para validar notificacoes locais.

Arquitetura editorial alvo do README:

1. explicar rapidamente o fluxo ponta a ponta e os componentes obrigatorios da
   PoC;
2. listar pre-requisitos reais e o caminho primario de bootstrap via Docker
   Compose;
3. orientar a pessoa usuaria a gerar o fluxo feliz com um exemplo minimo e
   reproduzivel;
4. mostrar onde inspecionar traces, metricas, logs, dashboard e alertas;
5. deixar explicita a separacao entre URLs do host e endpoints internos da rede
   Docker para evitar passos enganosos;
6. encerrar com troubleshooting curto e acionavel, sem reabrir escopo
   funcional.

Baseline validada e obrigatoria para o README final:

1. `lgtm` exposto em `http://localhost:3000` com `admin/admin`;
2. `order-service` exposto em `http://localhost:8080` com `GET /`,
   `GET /health`, `POST /orders` e `GET /orders/{id}`;
3. `otelcol` exposto no host em `localhost:4317` e `localhost:4318`;
4. dashboard versionado `OTel PoC - Service Metrics` com
   `uid: otel-poc-m3-overview`;
5. duas regras provisionadas: `OrderService P95 > 500 ms` e
   `ProcessingWorker lag > 100`;
6. contact point local apontando para `http://alert-webhook-mock:8080/`, sem
   publicacao de porta no host;
7. caminho primario da demonstracao sem dependencia de .NET 10 SDK no host.

---

## Design Decisions

### README unico e canonico, com ordem fixa de secoes

**Decision**: O `README.md` final deve se tornar a referencia unica da PoC e
seguir uma ordem fixa de leitura: visao geral, pre-requisitos, bootstrap,
fluxo feliz, traces, metricas/dashboard, logs, alertas e troubleshooting.

**Reason**: A feature precisa reduzir o conhecimento tacito acumulado durante
M1-M3 e transformar a baseline validada em um roteiro previsivel para primeira
execucao e demo.

**Trade-off**: O README ficara mais opinativo e menos enciclopedico. Isso e
intencional, porque a prioridade e reproducao local e demonstracao, nao uma
documentacao completa de implementacao interna.

### Arquitetura apresentada por narrativa curta e tabela de componentes, sem novo diagrama

**Decision**: A secao de arquitetura deve usar uma narrativa curta do fluxo e
uma tabela objetiva de componentes/portas/responsabilidades, sem criar novo
artefato visual ou diagrama dedicado nesta feature.

**Reason**: O milestone pede documentacao cirurgica e reaproveitamento da
baseline atual. Uma tabela simples comunica melhor a topologia real do compose e
reduz custo de manutencao.

**Trade-off**: O README perde apelo visual de um diagrama formal, mas ganha em
clareza operacional e evita drift com o runtime real.

### Exemplos de comando devem privilegiar copia e reproducao no host validado

**Decision**: O README deve usar comandos `docker compose` como estilo
canonico para bootstrap e diagnostico, e exemplos HTTP em PowerShell para os
passos com payload JSON e captura de `orderId`.

**Reason**: A baseline recente foi validada em ambiente Windows com PowerShell,
e esse formato evita ambiguidade de quoting em requests HTTP. Os comandos Docker
ja sao shell-friendly e permanecem estaveis.

**Trade-off**: O README fica menos neutro entre shells para chamadas HTTP. Em
troca, a documentacao permanece diretamente executavel no ambiente realmente
usado na validacao.

### Matriz host versus rede interna e obrigatoria

**Decision**: O README deve conter uma matriz explicita separando servicos
expostos no host daqueles acessiveis apenas na rede Docker, com orientacao de
uso por contexto.

**Reason**: O principal risco documental identificado em M4 e induzir URLs
`localhost` inexistentes para servicos internos, especialmente no fluxo de
alertas com o `alert-webhook-mock`.

**Trade-off**: A documentacao ganha uma secao mais operacional. Isso e aceito
porque evita troubleshooting artificial e passos impossiveis de reproduzir.

### Alertas devem ser verificados por sinais reais do ambiente atual

**Decision**: O README deve orientar a verificacao do receiver local por
`docker compose logs alert-webhook-mock` e, quando necessario, por inspecao
interna do endpoint `/requests` dentro da rede Docker, nunca por `localhost`.

**Reason**: O contact point provisionado usa `http://alert-webhook-mock:8080/`
e o servico nao publica porta no host. Qualquer instrucao diferente reabriria
escopo funcional ou produziria falsa expectativa.

**Trade-off**: A verificacao do webhook fica um pouco menos direta para quem
espera abrir uma URL no navegador, mas permanece fiel ao compose atual.

### Reutilizar evidencias versionadas e validacoes registradas, sem especulacao

**Decision**: Cada secao do README deve nascer de artefatos versionados e
validacoes ja consolidadas em `STATE.md`, sem introduzir comandos, portas,
credenciais ou comportamentos nao comprovados.

**Reason**: A feature e documental. O README precisa refletir a baseline atual,
nao empurrar mudancas futuras para o sistema.

**Trade-off**: O texto fica mais contido e menos ambicioso em exemplos. Em
troca, a documentacao ganha confiabilidade.

---

## Proposed File Layout

Escopo esperado da implementacao futura:

```text
README.md
```

Fontes de verdade que o README deve reutilizar, sem virar artefatos novos:

```text
docker-compose.yaml
grafana/dashboards/otel-poc-overview.json
grafana/provisioning/alerting/otel-poc-alert-rules.yaml
grafana/provisioning/alerting/otel-poc-contact-points.yaml
grafana/provisioning/alerting/otel-poc-notification-policies.yaml
tools/alert-webhook-mock/server.py
src/OrderService/Program.cs
.specs/project/ROADMAP.md
.specs/project/STATE.md
```

O design nao preve novos arquivos auxiliares, scripts de demo ou diagramas para
esta feature. O objetivo e consolidar a baseline existente no `README.md`.

---

## README Information Architecture

## Secao 1: O que esta PoC demonstra

Profundidade esperada:

1. um paragrafo curto explicando o objetivo da PoC;
2. um resumo do fluxo `POST /orders` -> Kafka -> workers -> PostgreSQL -> OTLP
   -> LGTM;
3. uma tabela curta com componentes, papel operacional e forma de acesso.

Conteudo obrigatorio:

1. mencionar `order-service`, `processing-worker`, `notification-worker`,
   `otelcol`, `lgtm`, `kafka`, `postgres` e `alert-webhook-mock`;
2. deixar claro que a stack de observabilidade e reutilizada pelo compose atual;
3. explicar que dashboard e alertas ja fazem parte da baseline validada.

## Secao 2: Pre-requisitos

Profundidade esperada:

1. lista curta e objetiva;
2. sem instalacao detalhada de ferramentas;
3. sem transformar o README em guia de setup de SDK local.

Conteudo obrigatorio:

1. Docker com suporte a `docker compose`;
2. portas `3000`, `8080`, `4317` e `4318` livres no host;
3. shell local para rodar Docker e requests HTTP;
4. nota explicita de que `.NET 10 SDK` no host e opcional e nao bloqueia a demo
   principal.

## Secao 3: Como subir o ambiente

Profundidade esperada:

1. um bloco de comando principal para bootstrap;
2. um bloco curto de verificacao pos-subida;
3. uma lista curta de resultados esperados.

Conteudo obrigatorio:

1. `docker compose up -d --build` como caminho primario;
2. verificacao basica por `docker compose ps` e logs quando necessario;
3. observacao sobre o tempo de aquecimento do Grafana/provisioning.

## Secao 4: Como gerar o fluxo feliz

Profundidade esperada:

1. um unico exemplo de request para `POST /orders`;
2. captura do `orderId` como ponte para os passos seguintes;
3. um `GET /orders/{id}` opcional para confirmacao;
4. nenhuma exploracao extensa de falhas nesta etapa.

Conteudo obrigatorio:

1. payload minimo com `description`;
2. resposta esperada com `orderId` e estado persistido;
3. explicacao de que o restante do fluxo segue automaticamente por Kafka e
   workers.

## Secao 5: Como inspecionar traces

Profundidade esperada:

1. indicar onde abrir o Grafana e o datasource Tempo;
2. descrever a expectativa minima do trace distribuido;
3. sugerir uma leitura operacional curta do caminho do pedido.

Conteudo obrigatorio:

1. `http://localhost:3000` com `admin/admin`;
2. expectativa de spans envolvendo `order-service`, `processing-worker` e
   `notification-worker`;
3. referencia ao fluxo HTTP -> Kafka -> HTTP -> Kafka -> DB.

## Secao 6: Como inspecionar metricas e dashboard

Profundidade esperada:

1. um caminho curto para o dashboard provisionado;
2. um resumo por servico dos sinais esperados;
3. uma referencia curta ao Explore/Prometheus para verificacao ad hoc.

Conteudo obrigatorio:

1. dashboard `OTel PoC - Service Metrics`;
2. `uid: otel-poc-m3-overview` como ancora tecnica para o texto;
3. sinais principais: throughput, latencia, backlog e consumer lag.

## Secao 7: Como inspecionar logs

Profundidade esperada:

1. uma explicacao curta de que o Loki e o destino preferencial;
2. fallback pragmatico com `docker compose logs` quando isso simplificar a demo;
3. foco em correlacao, nao em inventario de todas as mensagens.

Conteudo obrigatorio:

1. caminho via Grafana Explore + Loki;
2. sugestao de logs de container para diagnostico rapido;
3. mencao ao uso de `TraceId` ou correlacao temporal com o pedido criado.

## Secao 8: Como inspecionar alertas

Profundidade esperada:

1. localizar regras no Grafana;
2. dizer como verificar notificacoes reais do receiver;
3. explicar explicitamente o limite entre host e rede interna.

Conteudo obrigatorio:

1. nomes das regras `OrderService P95 > 500 ms` e
   `ProcessingWorker lag > 100`;
2. existencia do contact point `OTel PoC Local Webhook`;
3. instrucao de validacao por logs do `alert-webhook-mock`;
4. opcao de inspecao interna do endpoint `/requests` sem prometer URL local no
   host.

## Secao 9: Troubleshooting basico

Profundidade esperada:

1. bullets curtos por sintoma;
2. um comando ou verificacao por sintoma;
3. sem diagnostico profundo de aplicacao.

Conteudo obrigatorio:

1. conflito de portas no host;
2. containers ainda inicializando ou nao saudaveis;
3. dashboard/alertas ainda nao visiveis por atraso de provisioning;
4. ausencia de `.NET 10 SDK` no host nao bloqueando o fluxo principal.

---

## Command Style And Editorial Rules

### Regras de comandos

1. usar um bloco de comando por passo principal, evitando sequencias longas sem
   contexto;
2. priorizar comandos copiaveis como `docker compose up -d --build`,
   `docker compose ps` e `docker compose logs --tail=50 <service>`;
3. usar exemplos HTTP em PowerShell com payload JSON compacto e captura clara do
   `orderId`;
4. quando um passo depender de rede interna, deixar isso rotulado no texto antes
   do comando;
5. nao incluir comandos que dependam de alterar compose, publicar novas portas
   ou instalar ferramentas fora da baseline.

### Regras editoriais

1. manter tom pragmatico e orientado a execucao;
2. abrir cada secao com o objetivo do passo e encerrar apenas com a observacao
   minima esperada;
3. evitar historico do projeto, discussoes de implementacao interna e jargao
   desnecessario;
4. preferir listas curtas e tabelas a paragrafos longos;
5. usar nomes de servico e URLs exatamente como aparecem no compose atual.

---

## Host And Network Exposure Matrix

| Componente | Acesso no host | Endpoint interno na rede Docker | Como o README deve tratar |
| --- | --- | --- | --- |
| `lgtm` | `http://localhost:3000` | `http://lgtm:3000` | tratar `localhost:3000` como URL principal da demo |
| `order-service` | `http://localhost:8080` | `http://order-service:8080` | usar `localhost:8080` para requests manuais; citar endpoint interno apenas ao explicar dependencia do worker |
| `otelcol` | `localhost:4317` e `localhost:4318` | `http://otelcol:4317` e `http://otelcol:4318` | tratar como endpoint de telemetria da stack, nao como ponto de interacao manual da demo |
| `processing-worker` | nao exposto | `processing-worker` | marcar como servico interno, sem URL local |
| `notification-worker` | nao exposto | `notification-worker` | marcar como servico interno, sem URL local |
| `kafka` | nao exposto | `kafka:9092` | citar apenas como backbone interno do fluxo |
| `postgres` | nao exposto | `postgres:5432` | citar apenas como persistencia interna |
| `alert-webhook-mock` | nao exposto | `http://alert-webhook-mock:8080` | deixar explicito que a verificacao e por logs ou inspecao interna, nunca por `localhost` |
| `zookeeper` | nao exposto | `zookeeper:2181` | omitir do roteiro principal e citar apenas na topologia resumida |

Esta matriz deve aparecer cedo no README, antes das secoes de demo e alertas, de
forma que a diferenca entre host e rede interna fique resolvida antes da pessoa
usuaria executar comandos.

---

## Artifact Reuse Plan

### `docker-compose.yaml`

Usar como fonte de verdade para:

1. lista de servicos da PoC;
2. portas expostas no host;
3. dependencias entre servicos;
4. diferenciacao entre servicos expostos e internos.

### `src/OrderService/Program.cs`

Usar como fonte de verdade para:

1. endpoints HTTP disponiveis ao usuario da demo;
2. formato basico do fluxo feliz;
3. verificacoes minimas de `GET /`, `GET /health`, `POST /orders` e
   `GET /orders/{id}`.

### `grafana/dashboards/otel-poc-overview.json`

Usar como fonte de verdade para:

1. titulo do dashboard;
2. `uid` do dashboard;
3. escopo visual dos paines por servico;
4. coerencia entre texto do README e dashboard provisionado.

### `grafana/provisioning/alerting/otel-poc-alert-rules.yaml`

Usar como fonte de verdade para:

1. nomes exatos das regras;
2. thresholds documentados;
3. folder operacional das regras no Grafana.

### `grafana/provisioning/alerting/otel-poc-contact-points.yaml`

Usar como fonte de verdade para:

1. nome do contact point;
2. URL interna do receiver;
3. limitacao deliberada de acesso apenas na rede Docker.

### `grafana/provisioning/alerting/otel-poc-notification-policies.yaml`

Usar como fonte de verdade para:

1. existencia de policy tree unica da PoC;
2. roteamento minimo para o webhook local;
3. ausencia de canais externos na baseline.

### `tools/alert-webhook-mock/server.py`

Usar como fonte de verdade para:

1. endpoints internos `/health` e `/requests`;
2. modelo de verificacao do recebimento via logs e consulta interna;
3. comportamento do mock como apoio de ambiente, nao como servico exposto.

### `.specs/project/STATE.md`

Usar como fonte de verdade para:

1. restricoes de escopo da feature;
2. evidencias ja validadas em M1, M2 e M3;
3. decisoes arquiteturais sobre host versus rede interna.

---

## Task Boundaries For Next Iteration

O design deixa quatro fronteiras naturais para a futura quebra em tasks:

1. atualizar a abertura do `README.md` com visao geral, topologia resumida,
   pre-requisitos e bootstrap;
2. documentar o fluxo feliz com comandos HTTP, captura de `orderId` e roteiro de
   traces/metricas/dashboard;
3. documentar logs, alertas e a matriz host versus rede interna, incluindo o
   `alert-webhook-mock`;
4. revisar troubleshooting, coerencia editorial e aderencia estrita aos
   artefatos versionados da baseline.

Cada uma dessas fronteiras pode ser convertida em tasks pequenas e
verificaveis sem tocar codigo de aplicacao, collector, dashboards, alertas ou
compose funcional.