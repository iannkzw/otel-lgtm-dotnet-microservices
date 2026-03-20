# Gerador de Carga - Tasks

**Design**: `.specs/features/gerador-de-carga/design.md`
**Status**: Tasks Defined

---

## Execution Plan

### Phase 1: Entrypoint e contrato minimo

```text
T1 (entrypoint PowerShell) -> T2 (validacao de parametros e rota alvo)
T2 -> T3 (payload builder + modelo de resultado)
T3 -> T4 (executor HTTP unico)
```

### Phase 2: Modos de execucao e resumo

```text
T4 -> T5 (modo happy)
T4 + T2 -> T6 (modo latency)
T5 + T6 -> T7 (resumo final + exit code)
```

### Phase 3: README minimo e verificacao de escopo

```text
T7 -> T8 (referencia minima no README)
T7 + T8 -> T9 (validacao integrada e guarda de escopo)
```

---

## Task Breakdown

### T1: Criar o entrypoint canonico do gerador em PowerShell

**What**: Introduzir o unico entrypoint versionado do utilitario host-side em
`tools/load-generator/generate-orders.ps1`, preservando o MVP como helper
externo de demonstracao e evitando qualquer expansao estrutural fora do script.

**Where**: `tools/load-generator/generate-orders.ps1`

**Depends on**: feature `gerador-de-carga` design concluida

**Done when**:

- [ ] Existe o diretorio `tools/load-generator/` com o arquivo `generate-orders.ps1`
- [ ] O script expoe um unico bloco `param(...)` com `BaseUrl`, `Count`, `Mode`, `Concurrency`, `TimeoutSeconds` e `PauseMs`
- [ ] O utilitario continua totalmente fora de `src/` e nao exige modulo PowerShell, container dedicado, manifest ou arquivo de configuracao externo
- [ ] A implementacao planejada permanece limitada ao host-side em PowerShell, coerente com o ambiente Windows validado
- [ ] Nenhuma task derivada desta feature depende de alterar `src/`, `docker-compose.yaml`, `otelcol.yaml`, `processors/` ou `grafana/`

**Verification**:

- Local: a estrutura de arquivos planejada fica restrita a `tools/load-generator/generate-orders.ps1`
- Runtime: nao aplicavel nesta tarefa

---

### T2: Validar parametros e fixar a rota HTTP real do utilitario

**What**: Definir a validacao rapida dos parametros de entrada e a composicao da
rota alvo do script, garantindo que toda execucao aponte apenas para o endpoint
real `POST /orders` exposto pelo `OrderService`.

**Where**: `tools/load-generator/generate-orders.ps1`

**Depends on**: T1

**Done when**:

- [ ] `Count` e validado como maior que zero antes de qualquer envio
- [ ] `Concurrency` e validado como maior que zero
- [ ] `TimeoutSeconds` e validado como maior que zero
- [ ] `PauseMs` e validado como zero ou positivo
- [ ] `Mode` aceita apenas `happy` e `latency`
- [ ] O alvo HTTP do script e composto sempre como `BaseUrl + /orders`, sem endpoints auxiliares, sem `/health` e sem rotas especiais para demo
- [ ] O default operacional continua sendo `http://localhost:8080`, coerente com o `README.md` e com a baseline atual

**Verification**:

- Local: a rota alvo e o contrato de parametros batem com `README.md` e `src/OrderService/Program.cs`
- Runtime: entradas invalidas falham antes de gerar side effects no `OrderService`

---

### T3: Construir o payload real e o modelo normalizado de resultado

**What**: Planejar as funcoes internas que montam o JSON real aceito hoje pelo
`OrderService` e normalizam o resultado de cada tentativa para agregacao e
resumo final.

**Where**: `tools/load-generator/generate-orders.ps1`

**Depends on**: T2

**Done when**:

- [ ] Existe um builder interno que gera apenas o payload JSON com o campo `description`
- [ ] Cada request recebe uma `description` unica e legivel, com prefixo derivado do modo e GUID ou equivalente
- [ ] O utilitario nao introduz campos extras alem do contrato atual de `CreateOrderRequest`
- [ ] Existe um modelo de resultado por request contendo ao menos `sequence`, `mode`, `description`, `succeeded`, `statusCode`, `durationMs`, `orderId` e `error`
- [ ] O modelo de resultado e suficiente para resumir sucessos, falhas HTTP e falhas de transporte sem parsing posterior de texto cru

**Verification**:

- Local: o payload planejado corresponde ao contrato atual em `src/OrderService/Contracts/CreateOrderRequest.cs`
- Runtime: nao aplicavel isoladamente nesta tarefa

---

### T4: Implementar o executor HTTP unico de `POST /orders`

**What**: Separar uma funcao unica para executar uma request real contra o
`OrderService`, medir a duracao local e devolver um resultado normalizado que
sera reutilizado pelos modos `happy` e `latency`.

**Where**: `tools/load-generator/generate-orders.ps1`

**Depends on**: T3

**Done when**:

- [ ] Existe uma funcao dedicada para enviar uma unica request a `POST /orders`
- [ ] A funcao usa apenas cmdlets nativos do PowerShell e timeout configuravel por parametro
- [ ] Respostas bem-sucedidas extraem `orderId` quando presente no payload retornado
- [ ] Respostas nao `2xx`, timeout e falhas de transporte sao normalizados em um resultado consistente
- [ ] Nao existe retry sofisticado, fila local, endpoint alternativo nem alteracao do comportamento do servidor

**Verification**:

- Local: a implementacao continua ancorada no endpoint real documentado em `src/OrderService/Program.cs`
- Runtime: uma chamada isolada bem-sucedida retorna resultado com `succeeded=true`; falha de endpoint indisponivel retorna resultado com `error` preenchido

---

### T5: Implementar o modo `happy` como fluxo sequencial da demo

**What**: Planejar o caminho principal de demonstracao para enviar requests
sequenciais, priorizando simplicidade operacional, leitura no terminal e
popular traces, metricas, logs e dashboard sem artificios extras.

**Where**: `tools/load-generator/generate-orders.ps1`

**Depends on**: T4

**Done when**:

- [ ] O modo `happy` envia exatamente `Count` requests sequenciais
- [ ] Cada request do modo `happy` usa `description` com prefixo coerente, como `happy-...`
- [ ] O fluxo principal do `README.md` pode usar esse modo como comando canonico de geracao de carga
- [ ] O modo `happy` nao depende de jobs, paralelismo nem benchmark tooling
- [ ] `PauseMs` pode ser respeitado entre requests sem mudar a semantica simples do modo

**Verification**:

- Local: a logica planejada do modo `happy` depende apenas do executor comum e do payload builder
- Runtime: com `Count` pequeno, o modo gera novos pedidos reais e deixa sinais observaveis na baseline existente

---

### T6: Implementar o modo `latency` com densidade client-side controlada

**What**: Planejar o modo opcional voltado a aumentar a pressao de requests no
cliente PowerShell, mantendo o escopo restrito a batches e concorrencia simples
contra o mesmo endpoint real do `OrderService`.

**Where**: `tools/load-generator/generate-orders.ps1`

**Depends on**: T2, T4

**Done when**:

- [ ] O modo `latency` reutiliza exatamente o mesmo executor de `POST /orders` do modo `happy`
- [ ] O aumento de densidade e controlado por `Count`, `Concurrency` e opcionalmente `PauseMs` entre batches
- [ ] O paralelismo client-side usa apenas recursos nativos do PowerShell, como jobs simples, quando necessario
- [ ] O modo nao exige k6, JMeter, hey, vegeta, container temporario ou qualquer tooling externa de benchmark
- [ ] O modo nao depende de flags escondidas, sleeps internos no servidor, endpoints especiais ou mudancas na aplicacao

**Verification**:

- Local: a estrategia planejada do modo `latency` continua restrita ao script host-side
- Runtime: com `Count` e `Concurrency` maiores que no modo `happy`, o script produz carga mais densa e utilizavel para observar o alerta existente

---

### T7: Consolidar a saida de console, o resumo final e os codigos de saida

**What**: Planejar a camada final de apresentacao do utilitario para que cada
execucao termine com um resumo curto, observavel e suficiente para diagnosticar
sucessos parciais, falhas totais e duracoes agregadas.

**Where**: `tools/load-generator/generate-orders.ps1`

**Depends on**: T5, T6

**Done when**:

- [ ] Cada request pode emitir uma linha sucinta com sequencia, status, duracao e `orderId` quando houver sucesso
- [ ] O resumo final informa ao menos `mode`, `requested`, `succeeded`, `failed`, `totalMs`, `avgMs`, `minMs` e `maxMs`
- [ ] O resumo final existe mesmo com falhas parciais
- [ ] O exit code e diferente de zero quando nenhuma request obtiver sucesso ou quando o endpoint estiver indisponivel durante toda a execucao
- [ ] O desenho da saida continua textual e enxuto, sem gerar CSV, JSON de output, banco local ou arquivos auxiliares

**Verification**:

- Local: o resumo agregado pode ser derivado diretamente do modelo de resultado definido em T3
- Runtime: execucoes com sucesso parcial ou falha total continuam produzindo resumo final compreensivel

---

### T8: Ajustar o `README.md` com referencia minima ao gerador

**What**: Planejar o ajuste minimo no `README.md` para incorporar o gerador de
carga como helper versionado da demo, sem criar roteiro concorrente nem
duplicar explicacoes que ja pertencem ao README canonico.

**Where**: `README.md`

**Depends on**: T7

**Done when**:

- [ ] O `README.md` passa a citar o comando principal do modo `happy` usando `tools/load-generator/generate-orders.ps1`
- [ ] Existe referencia curta ao modo `latency` como opcao de apoio para a demonstracao do alerta `OrderService P95 > 500 ms`
- [ ] O texto deixa claro que o script usa o endpoint real `http://localhost:8080/orders`
- [ ] O README continua sendo o roteiro canonico da demo; o script aparece apenas como helper para gerar carga
- [ ] O ajuste documental continua pequeno e restrito ao necessario para primeira execucao da feature

**Verification**:

- Local: a nova referencia do README permanece coerente com a baseline validada e com o design da feature
- Runtime: os comandos documentados sao reproduziveis no ambiente Windows ja validado

---

### T9: Validar a implementacao futura contra a baseline e a fronteira de escopo

**What**: Fechar a feature com uma revisao integrada que confirme aderencia ao
design, uso exclusivo do endpoint e contrato reais, e ausencia de churn fora do
helper externo e do ajuste minimo no README.

**Where**: `tools/load-generator/generate-orders.ps1`, `README.md`

**Depends on**: T7, T8

**Done when**:

- [ ] O diff funcional da feature fica restrito a `tools/load-generator/generate-orders.ps1` e ao ajuste minimo em `README.md`
- [ ] A execucao default do script continua apontando para `http://localhost:8080/orders`
- [ ] O payload usado pelo utilitario continua restrito ao campo `description`
- [ ] Nao ha mudancas em `src/`, `docker-compose.yaml`, `otelcol.yaml`, `processors/` ou `grafana/`
- [ ] Existe validacao final de pelo menos um smoke test em `happy` e uma execucao controlada em `latency`, desde que o ambiente local esteja disponivel
- [ ] O resultado final permanece compativel com a baseline validada de M1, M2 e M3 e nao reabre escopo funcional da PoC

**Verification**:

- Local: revisao de diff confirma a fronteira restrita da feature e a aderencia ao design
- Runtime: `happy` e `latency` executam contra o ambiente existente sem exigir alteracoes adicionais na stack

---

## Implementation Notes For Next Iteration

Fronteiras da implementacao futura:

1. a proxima iteracao deve tocar apenas `tools/load-generator/generate-orders.ps1` e o ajuste minimo correspondente em `README.md`;
2. a ordem recomendada de execucao e T1 -> T2 -> T3 -> T4 -> T5/T6 -> T7 -> T8 -> T9;
3. a validacao final deve conferir explicitamente o endpoint `http://localhost:8080/orders`, o contrato atual `description` e a ausencia de mudancas em `src/`, `docker-compose.yaml`, `otelcol.yaml`, `processors/` e `grafana/`;
4. o modo `happy` continua sendo o caminho principal do README, enquanto o modo `latency` permanece um helper pequeno para apoiar a observacao do alerta ja provisionado.