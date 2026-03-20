# README da PoC - Tasks

**Design**: `.specs/features/readme-poc/design.md`
**Status**: Tasks Defined

---

## Execution Plan

### Phase 1: Reestruturacao editorial e baseline operacional

```text
T1 (mapa editorial do README) -> T2 (arquitetura + pre-requisitos + bootstrap)
T2 -> T3 (matriz host vs rede interna)
```

### Phase 2: Roteiro de demonstracao e navegacao de sinais

```text
T2 + T3 -> T4 (fluxo feliz)
T4 -> T5 (traces)
T4 -> T6 (metricas + dashboard)
T4 -> T7 (logs)
T3 + T4 -> T8 (alertas + webhook mock)
```

### Phase 3: Fechamento documental e verificacao

```text
T5 + T6 + T7 + T8 -> T9 (troubleshooting)
T1 + T9 -> T10 (revisao final e aderencia a baseline)
```

---

## Task Breakdown

### T1: Reestruturar o README como documento canonico da PoC

**What**: Reescrever a estrutura do `README.md` para seguir a ordem editorial
aprovada no design, removendo o foco residual na baseline anterior do stack base
e preparando o documento para primeira execucao, demo e referencia rapida.

**Where**: `README.md`

**Depends on**: feature `readme-poc` design concluida

**Done when**:

- [ ] O `README.md` deixa de afirmar que os servicos .NET ainda nao entram no compose
- [ ] A ordem macro das secoes passa a ser: visao geral, pre-requisitos, bootstrap, fluxo feliz, traces, metricas/dashboard, logs, alertas e troubleshooting
- [ ] O texto inicial do README passa a descrever a PoC atual de M1, M2 e M3, sem recontar o historico do projeto
- [ ] O README fica claramente orientado a reproducao local e demonstracao, nao a implementacao interna detalhada
- [ ] Nenhuma secao nova depende de alterar compose, collector, dashboards, alertas ou servicos .NET

**Verification**:

- Local: leitura do `README.md` mostra a nova ordem editorial e elimina contradicoes com a baseline atual
- Runtime: nao aplicavel nesta tarefa

---

### T2: Documentar arquitetura resumida, pre-requisitos reais e bootstrap do ambiente

**What**: Atualizar o inicio do `README.md` com a explicacao curta da PoC, a
lista objetiva de componentes obrigatorios, os pre-requisitos reais do host e o
fluxo minimo de subida por `docker compose up -d --build`.

**Where**: `README.md`

**Depends on**: T1

**Done when**:

- [ ] O README explica o fluxo `POST /orders` -> Kafka -> workers -> PostgreSQL -> OTLP -> LGTM em linguagem curta e operacional
- [ ] O texto menciona explicitamente `order-service`, `processing-worker`, `notification-worker`, `otelcol`, `lgtm`, `kafka`, `postgres` e `alert-webhook-mock`
- [ ] A lista de pre-requisitos inclui Docker com `docker compose`, portas `3000`, `8080`, `4317` e `4318` e shell local
- [ ] Existe nota explicita de que `.NET 10 SDK` no host e opcional para a demo principal
- [ ] O bootstrap usa `docker compose up -d --build` como caminho primario e inclui uma verificacao basica com `docker compose ps`
- [ ] O README orienta que o provisionamento do Grafana pode levar alguns instantes apos a subida inicial

**Verification**:

- Local: os comandos e URLs citados batem com `docker-compose.yaml`
- Runtime: os comandos documentados sao compativeis com o ambiente validado atual

---

### T3: Consolidar a matriz de host versus rede interna

**What**: Inserir no `README.md` a matriz obrigatoria que separa servicos
expostos no host de endpoints acessiveis apenas na rede Docker, com orientacao
explicita de uso por contexto.

**Where**: `README.md`

**Depends on**: T2

**Done when**:

- [ ] O README distingue claramente `localhost:3000` e `localhost:8080` dos endpoints internos usados entre containers
- [ ] `processing-worker`, `notification-worker`, `kafka`, `postgres`, `zookeeper` e `alert-webhook-mock` aparecem como internos quando apropriado
- [ ] O `alert-webhook-mock` e descrito explicitamente como nao exposto no host
- [ ] O texto evita instrucoes enganosas que sugiram abrir o webhook mock em `localhost`
- [ ] A matriz aparece antes das secoes de demo e alertas, reduzindo ambiguidade para quem executa os comandos

**Verification**:

- Local: todas as portas e nomes de servico coincidem com `docker-compose.yaml`
- Runtime: nao aplicavel isoladamente nesta tarefa

---

### T4: Documentar o fluxo feliz minimo com comandos reproduziveis

**What**: Escrever no `README.md` o roteiro minimo da demo para criar um pedido,
capturar `orderId` e opcionalmente consultar o estado persistido sem introduzir
casos de erro, scripts auxiliares ou passos manuais extras entre os hops Kafka.

**Where**: `README.md`

**Depends on**: T2, T3

**Done when**:

- [ ] O README inclui um exemplo de `POST /orders` para `http://localhost:8080/orders`
- [ ] O exemplo usa payload minimo com `description`
- [ ] O texto explica como capturar ou reaproveitar o `orderId` retornado
- [ ] Existe verificacao opcional de `GET /orders/{id}` coerente com os endpoints reais do `OrderService`
- [ ] O README deixa claro que o restante do fluxo segue automaticamente por Kafka e workers
- [ ] O estilo do exemplo HTTP e consistente com PowerShell e quoting executavel no ambiente validado

**Verification**:

- Local: os endpoints documentados existem em `src/OrderService/Program.cs`
- Runtime: o roteiro e reproduzivel no host atual sem alterar o sistema

---

### T5: Documentar a inspecao de traces no Grafana Tempo

**What**: Adicionar ao `README.md` o caminho operacional para abrir o Grafana,
acessar o Tempo e localizar o trace distribuido gerado pelo fluxo feliz do
pedido.

**Where**: `README.md`

**Depends on**: T4

**Done when**:

- [ ] O README usa `http://localhost:3000` com credenciais `admin/admin`
- [ ] A secao orienta a usar o Explore do Grafana com datasource Tempo
- [ ] O texto descreve a expectativa minima de hops entre `order-service`, `processing-worker` e `notification-worker`
- [ ] A leitura operacional do trace menciona o caminho HTTP -> Kafka -> HTTP -> Kafka -> DB sem inventar spans novos
- [ ] Nao ha dependencia de nomes de span nao validados ou filtros de alta cardinalidade

**Verification**:

- Local: a secao cita apenas servicos, credenciais e caminhos coerentes com a baseline documentada
- Runtime: a expectativa descrita bate com o trace distribuido ja validado em M2

---

### T6: Documentar metricas e o dashboard provisionado da PoC

**What**: Atualizar o `README.md` com o caminho minimo para o dashboard
versionado e para o Explore/Prometheus, explicando em alto nivel os sinais
esperados por servico.

**Where**: `README.md`

**Depends on**: T4

**Done when**:

- [ ] O README referencia o dashboard `OTel PoC - Service Metrics`
- [ ] O texto ancora tecnicamente o dashboard com `uid: otel-poc-m3-overview`
- [ ] A secao resume os sinais por servico: throughput, latencia, backlog e consumer lag
- [ ] O texto orienta a usar o Explore do Grafana com Prometheus para verificacao ad hoc
- [ ] Nao ha mencao a metricas novas, alertas extras ou dashboards diferentes da baseline versionada

**Verification**:

- Local: o nome e o `uid` citados batem com `grafana/dashboards/otel-poc-overview.json`
- Runtime: a navegacao descrita e compativel com o Grafana provisionado atual

---

### T7: Documentar a inspecao de logs sem abrir taxonomia desnecessaria

**What**: Inserir no `README.md` uma secao curta para localizar logs no Loki e,
quando isso simplificar a demo, recorrer a logs de container para diagnostico
rapido e correlacao pratica com o pedido criado.

**Where**: `README.md`

**Depends on**: T4

**Done when**:

- [ ] O README indica o Explore do Grafana com datasource Loki como caminho preferencial
- [ ] Existe fallback pragmatico com `docker compose logs` para diagnostico rapido
- [ ] O texto foca em correlacao pratica do fluxo e nao em catalogar todas as mensagens possiveis
- [ ] A secao nao promete filtros ou labels de log nao validados explicitamente
- [ ] A redacao continua enxuta e operacional

**Verification**:

- Local: os comandos e destinos de log descritos sao coerentes com a stack atual
- Runtime: a abordagem e compativel com a baseline validada de logs no LGTM

---

### T8: Documentar alertas Grafana e a verificacao correta do webhook mock

**What**: Atualizar o `README.md` com o caminho para as regras provisionadas,
o contact point local e a forma correta de verificar o receiver interno sem
induzir URLs `localhost` inexistentes.

**Where**: `README.md`

**Depends on**: T3, T4

**Done when**:

- [ ] O README cita as regras `OrderService P95 > 500 ms` e `ProcessingWorker lag > 100`
- [ ] O texto menciona o contact point `OTel PoC Local Webhook`
- [ ] A secao explica explicitamente que `alert-webhook-mock` nao esta exposto no host
- [ ] A verificacao principal do receiver usa `docker compose logs alert-webhook-mock`
- [ ] Existe opcao de inspecao interna do endpoint `/requests` sem sugerir acesso por `localhost`
- [ ] O texto nao sugere publicar novas portas nem alterar o compose para fins de documentacao

**Verification**:

- Local: nomes de regras e contact point batem com os YAMLs versionados em `grafana/provisioning/alerting/`
- Runtime: o metodo de verificacao e coerente com o receiver interno implementado em `tools/alert-webhook-mock/server.py`

---

### T9: Fechar o README com troubleshooting basico e acionavel

**What**: Inserir no `README.md` um bloco curto de troubleshooting cobrindo os
principais sintomas esperados na execucao da demo, sem transformar o documento
em manual de suporte profundo.

**Where**: `README.md`

**Depends on**: T5, T6, T7, T8

**Done when**:

- [ ] O troubleshooting cobre conflito de portas do host
- [ ] O troubleshooting cobre containers ainda inicializando ou nao saudaveis
- [ ] O troubleshooting cobre atraso de provisionamento para dashboard e alertas
- [ ] O troubleshooting reforca que a ausencia de `.NET 10 SDK` no host nao bloqueia a demo via Docker
- [ ] Cada sintoma tem orientacao curta e verificavel, sem abrir escopo para debug profundo de codigo

**Verification**:

- Local: os sintomas e comandos citados sao consistentes com a baseline real do ambiente
- Runtime: nao aplicavel isoladamente nesta tarefa

---

### T10: Revisar o README final contra a baseline e contra o escopo estritamente documental

**What**: Fazer a revisao final do `README.md` para garantir consistencia
editorial, aderencia aos artefatos versionados e ausencia de instrucoes que
reabram escopo funcional.

**Where**: `README.md`

**Depends on**: T1, T9

**Done when**:

- [ ] Todas as URLs, portas, credenciais e nomes de servico citados batem com a baseline atual
- [ ] O README nao depende de scripts novos, portas novas, SDK obrigatorio no host ou mudancas de comportamento do sistema
- [ ] O texto reutiliza apenas artefatos e evidencias ja consolidados em M1, M2 e M3
- [ ] A separacao entre host e rede interna permanece consistente em todo o documento
- [ ] O README final fica curto o bastante para demo, mas completo o bastante para primeira execucao sem ajuda externa

**Verification**:

- Local: leitura final do `README.md` nao revela contradicoes com `docker-compose.yaml`, `src/OrderService/Program.cs`, dashboard versionado, alertas provisionados e `STATE.md`
- Runtime: os comandos principais documentados permanecem reproduziveis no ambiente atual

---

## Implementation Notes For Next Iteration

Fronteiras da implementacao futura:

1. a feature deve tocar apenas `README.md`;
2. a ordem recomendada de execucao para a proxima iteracao e T1 -> T2 -> T3 -> T4 -> T5/T6/T7/T8 -> T9 -> T10;
3. cada secao implementada deve ser conferida diretamente contra os artefatos versionados da baseline, sem extrapolar comportamento;
4. a validacao final deve confirmar especialmente a matriz host versus rede interna e o fluxo de verificacao do `alert-webhook-mock`.