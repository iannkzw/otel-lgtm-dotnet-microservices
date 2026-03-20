# Gerador de Carga - Specification

**Milestone**: M4 - Hardening e Documentacao da PoC
**Status**: Specified

---

## Problem Statement

O milestone M4 ja posiciona o `README.md` como roteiro canonico da PoC, mas a
demonstracao local ainda depende de requests manuais isoladas ou de loops ad hoc
usados durante a validacao recente. Isso funciona para testes pontuais, mas nao
fecha bem a reproducibilidade da demo quando a meta e popular traces, metricas,
logs, dashboard e criar condicoes praticas para observar os alertas ja
provisionados.

Esta feature deve especificar um gerador de carga minimo, versionado e externo a
aplicacao, capaz de disparar N requests reais contra
`http://localhost:8080/orders` sem introduzir novas dependencias exoticas no
host nem reabrir a baseline validada de M1, M2 e M3. O papel da feature e
complementar o README canonico com automacao pragmatica de demo, nao criar um
novo fluxo operacional em paralelo nem transformar a PoC em suite de benchmark.

## Goals

- [ ] Especificar a feature `gerador-de-carga` como proximo passo de M4,
      complementando o `README.md` canonico com uma forma simples e
      reproduzivel de gerar carga local
- [ ] Definir um modo minimo de carga voltado ao fluxo feliz, gerando N requests
      validas para `POST /orders` e alimentando os sinais ja existentes da PoC
- [ ] Definir um modo opcional de pressao de latencia para apoiar a demonstracao
      da regra `OrderService P95 > 500 ms`, sem alterar codigo, contratos ou
      configuracao dos servicos
- [ ] Escolher um formato de implementacao pragmatico para o ambiente atual da
      PoC, evitando .NET 10 SDK no host como pre-requisito obrigatorio
- [ ] Produzir criterios de aceite claros que separem um utilitario externo de
      demonstracao de qualquer mudanca funcional na aplicacao ou na camada de
      observabilidade

## Out of Scope

- Novas metricas, novos spans, novos logs estruturados, novos paineis,
  dashboards adicionais ou novas regras de alerta
- Alteracoes em `docker-compose.yaml` apenas para acomodar o gerador de carga,
  incluindo novos servicos dedicados, novas portas publicadas ou sidecars
- Alteracoes em `otelcol.yaml`, processors em `processors/`, exporters,
  pipelines OTLP, tail sampling ou baseline da stack LGTM
- Mudancas em contratos HTTP, contratos Kafka, payloads, topicos, persistencia,
  schemas, banco, retries, DLQ, outbox ou comportamento dos servicos .NET
- Implementar benchmark complexo, teste de performance formal, relatorios
  extensos, comparacoes entre ambientes ou metas de throughput de producao
- Exigir ferramentas exoticas no host para concluir a demo local

---

## Current Baseline To Reuse

### Infraestrutura e fluxo real ja existentes

- `docker compose up -d --build` e o caminho primario para subir a PoC
- `order-service` esta exposto no host em `http://localhost:8080`
- `POST /orders` ja cria pedidos reais e aciona o fluxo distribuido completo da
  PoC
- `processing-worker` e `notification-worker` continuam operando apenas na rede
  Docker, sem endpoints HTTP no host
- Dashboard e alertas da PoC ja estao provisionados e validados na baseline

### Artefatos que a feature deve reutilizar

- `README.md` como roteiro canonico da demo local
- `docker-compose.yaml` da raiz como ponto unico de bootstrap do ambiente
- Endpoint real `http://localhost:8080/orders` e payload JSON atual do
  `OrderService`
- Alertas provisionados em `grafana/provisioning/alerting/`, especialmente a
  regra `OrderService P95 > 500 ms`
- Evidencia de validacao recente baseada em comandos PowerShell com
  `Invoke-WebRequest` e medicoes simples de latencia no host Windows

### Fronteira funcional da feature

- O gerador de carga deve permanecer fora do compose funcional da aplicacao
- O gerador nao pode se tornar fonte de verdade da demo; o roteiro canonico
  continua no `README.md`
- A feature deve apenas automatizar o passo de gerar carga local, reaproveitando
  a baseline ja estabilizada

---

## Candidate Formats

## Opcao A: Script PowerShell versionado no repositorio

**Descricao:** script host-side executado depois do `docker compose up`, usando
`Invoke-RestMethod` ou `Invoke-WebRequest` para chamar
`http://localhost:8080/orders`.

**Pros:**

- zero dependencia adicional no ambiente Windows ja validado
- usa `localhost:8080` diretamente, sem traducoes de rede entre host e
  container
- reaproveita exatamente o padrao de comando ja usado nas validacoes recentes
- evita exigir SDK .NET, k6, JMeter, hey, vegeta ou ferramentas semelhantes

**Cons:**

- privilegia o host Windows como caminho principal de MVP
- nao entrega paridade imediata para shells POSIX fora do Windows

## Opcao B: Script bash/curl host-side

**Descricao:** script shell simples chamando `curl` contra
`http://localhost:8080/orders`.

**Pros:**

- simples para ambientes POSIX
- facil de ler e de integrar ao README

**Cons:**

- no ambiente atual, tende a puxar WSL, Git Bash ou shell adicional no host
- criaria dois roteiros quase equivalentes de execucao para a mesma demo

## Opcao C: Container temporario para gerar carga

**Descricao:** `docker run` com imagem simples de HTTP client ou script embutido
para disparar requests.

**Pros:**

- reduz dependencia de shell especifico no host
- mantem a distribuicao do utilitario desacoplada do SDK .NET

**Cons:**

- no host Windows, o alvo deixa de ser naturalmente `localhost:8080` e passa a
  exigir detalhes como `host.docker.internal`, aumentando o atrito da demo
- incentiva workaround de rede ou alteracao de compose so para o gerador
- adiciona variacao desnecessaria a um utilitario que precisa ser minimo

## Recommendation

O MVP desta feature deve adotar a **Opcao A**, com um script PowerShell
versionado no repositorio como entrada canonica do gerador de carga. Essa
direcao e a mais aderente ao ambiente validado da PoC porque:

- usa o endpoint do host exatamente como ja documentado no README:
  `http://localhost:8080/orders`
- nao exige SDK .NET no host nem ferramenta externa de benchmark
- reaproveita diretamente os comandos PowerShell ja usados para validar a
  baseline recente
- evita criar um segundo roteiro de rede ou um compose paralelo para um helper
  que precisa ser pequeno

Paridade futura com bash/curl pode existir como extensao posterior, mas nao faz
parte do MVP desta spec.

---

## Required Minimal Operator Flow

O resultado desta feature deve convergir para um fluxo minimo como este:

1. subir a PoC pela raiz com `docker compose up -d --build`;
2. executar um comando unico do gerador contra `http://localhost:8080/orders`;
3. usar o `README.md` para navegar ate traces, metricas, logs, dashboard e
   alertas com base na carga produzida.

### Comando minimo esperado para o modo feliz

```powershell
powershell -File .\tools\load-generator\generate-orders.ps1 -BaseUrl http://localhost:8080 -Count 20 -Mode happy
```

### Comando opcional esperado para pressao de latencia

```powershell
powershell -File .\tools\load-generator\generate-orders.ps1 -BaseUrl http://localhost:8080 -Count 120 -Mode latency -Concurrency 6
```

O contrato exato dos parametros pode ser refinado no design, mas a spec exige
desde ja:

- `BaseUrl` apontando para o host local
- `Count` para controlar quantas requests enviar
- `Mode` com pelo menos `happy` e `latency`
- saida textual simples com total de requests, sucessos, falhas e algum resumo
  minimo de latencia ou tempo total

---

## User Stories

### P1: Gerar carga de fluxo feliz ⭐ MVP

**User Story**: Como pessoa operando a demo local, eu quero disparar N requests
reais para `POST /orders` com um comando simples para popular traces, metricas,
logs e dashboard sem repetir chamadas manuais uma a uma.

**Why P1**: Sem esse fluxo, a demonstracao depende de loops ad hoc e perde
reproducibilidade justamente no momento em que M4 precisa consolidar o roteiro
canonicamente no README.

**Acceptance Criteria**:

1. WHEN a pessoa usuaria executar o gerador em modo `happy` com `Count = N`
   THEN o utilitario SHALL enviar N requests validas para
   `http://localhost:8080/orders` usando o payload real aceito hoje pelo
   `OrderService`
2. WHEN as requests forem processadas THEN o utilitario SHALL permanecer externo
   aos servicos .NET e nao SHALL exigir qualquer mudanca em compose, collector,
   dashboards, alertas, contratos Kafka, payloads ou persistencia
3. WHEN houver respostas nao `2xx` ou falhas de conexao THEN o utilitario SHALL
   reportar as falhas na propria execucao, sem introduzir retry sofisticado,
   fila interna ou alteracao funcional na aplicacao

**Independent Test**: Rodar o comando em modo `happy` com `Count` pequeno,
observar novos pedidos no `order-service` e confirmar atividade recente em
Tempo, Prometheus/LGTM, Loki e dashboard da PoC.

---

### P2: Pressionar a latencia do OrderService sem alterar a aplicacao

**User Story**: Como pessoa apresentando a PoC, eu quero um modo opcional de
carga mais agressiva para aumentar a pressao sobre o `OrderService` e apoiar a
demonstracao do alerta de latencia ja existente.

**Why P2**: O alerta `OrderService P95 > 500 ms` ja foi provisionado e validado,
mas a demonstracao operacional fica mais reproduzivel se houver um modo de carga
controlado e reutilizavel em vez de loops improvisados no shell.

**Acceptance Criteria**:

1. WHEN a pessoa usuaria executar o gerador em modo `latency` THEN o utilitario
   SHALL aumentar a densidade de requests por burst, concorrencia ou cadencia
   configuravel, ainda contra o mesmo endpoint real do `OrderService`
2. WHEN o modo `latency` for usado THEN o utilitario SHALL continuar sendo um
   helper externo e nao SHALL depender de sleeps artificiais dentro da
   aplicacao, flags escondidas, endpoints especiais ou alteracoes nos servicos
3. WHEN o ambiente nao atingir `firing` imediatamente THEN a feature SHALL ainda
   ser considerada valida desde que o modo produza carga repetivel e compativel
   com a validacao operacional da regra existente

**Independent Test**: Rodar o modo `latency` durante a observacao do alerta
`OrderService P95 > 500 ms` e verificar que a carga produzida e suficiente para
movimentar o sinal e apoiar a navegacao operacional da demo.

---

### P3: Integrar o gerador ao README sem criar roteiro concorrente

**User Story**: Como pessoa lendo o `README.md`, eu quero encontrar um unico
ponto de entrada para gerar carga local sem ter que escolher entre roteiros
conflitantes ou duplicados.

**Why P3**: M4 depende de um README canonico. Se o gerador trouxer documentacao
paralela demais, a feature perde o beneficio e reintroduz ambiguidade.

**Acceptance Criteria**:

1. WHEN a feature for implementada THEN o `README.md` SHALL continuar sendo a
   fonte canonica do walkthrough da PoC
2. WHEN o gerador for referenciado na documentacao THEN o README SHALL apontar
   para um comando principal e nao SHALL duplicar a logica interna do utilitario
3. WHEN a pessoa usuaria seguir o README THEN ela SHALL conseguir usar o gerador
   como apoio a demonstracao sem ter de aprender um fluxo alternativo de setup

**Independent Test**: Ler a secao correspondente do README, executar o comando
principal do gerador e voltar ao proprio README para inspecionar os sinais da
PoC.

---

## Edge Cases

- WHEN `Count` for zero, negativo ou invalido THEN o utilitario SHALL falhar
  rapido com erro de validacao local, sem disparar requests
- WHEN `http://localhost:8080/orders` estiver indisponivel THEN o utilitario
  SHALL encerrar com falha observavel e resumo simples do que nao foi enviado
- WHEN parte das requests falhar e parte tiver sucesso THEN o utilitario SHALL
  concluir com resumo agregado, sem mascarar falhas individuais
- WHEN o modo `latency` for executado em maquina lenta ou ambiente ainda frio
  THEN a feature SHALL continuar limitada a gerar carga repetivel, sem prometer
  thresholds absolutos de performance

---

## README Interaction Model

- O `README.md` permanece o documento canonico da demo local
- O gerador de carga entra como helper versionado, referenciado pelo README para
  automatizar o passo de gerar requests
- A documentacao da feature nao deve criar dois roteiros distintos de demo; ela
  deve apenas reduzir o atrito do passo de carga dentro do roteiro ja existente
- O README continua responsavel por bootstrap, navegacao de observabilidade e
  troubleshooting; o gerador continua responsavel apenas por emitir requests

---

## Implementation Boundaries For Future Steps

- A implementacao deve ficar em area utilitaria do repositorio, como
  `tools/load-generator/`, sem tocar `src/`
- Nenhum artefato em `docker-compose.yaml`, `otelcol.yaml`, `processors/`,
  `grafana/` ou contratos dos servicos deve precisar mudar para concluir a
  feature
- O gerador deve operar exclusivamente sobre o endpoint HTTP real do
  `OrderService`
- O utilitario deve continuar pequeno e orientado a demo local, nao a benchmark
  formal

---

## Success Criteria

Como saber que a feature esta bem especificada:

- [ ] Existe uma recomendacao pragmatica e explicita de formato para o MVP,
      aderente ao ambiente atual da PoC
- [ ] A spec define um comando minimo para gerar N requests reais contra
      `http://localhost:8080/orders` sem ferramenta exotica no host
- [ ] A spec cobre pelo menos um modo `happy` e um modo opcional `latency`
- [ ] Fica explicito que a feature reutiliza o compose da raiz, o endpoint real
      do `OrderService`, o README canonico e os alertas Grafana ja provisionados
- [ ] Fica explicito que a implementacao nao deve alterar collector,
      dashboards, alertas, contratos Kafka, payloads, persistencia ou codigo dos
      servicos .NET
- [ ] O papel do gerador em relacao ao README fica claro o bastante para evitar
      dois roteiros conflitantes de demonstracao