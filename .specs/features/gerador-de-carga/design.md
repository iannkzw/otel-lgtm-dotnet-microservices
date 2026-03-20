# Gerador de Carga - Design

**Spec**: `.specs/features/gerador-de-carga/spec.md`
**Status**: Designed

---

## Architecture Overview

Esta feature continua totalmente fora da aplicacao e da stack de
observabilidade. O fluxo funcional da PoC permanece identico ao validado em M1,
M2 e M3:

1. o ambiente continua subindo por `docker compose up -d --build` a partir do
   compose unico da raiz;
2. o `order-service` continua expondo `POST /orders` no host em
   `http://localhost:8080`;
3. `processing-worker` e `notification-worker` continuam consumindo e
   persistindo o fluxo distribuido sem alteracao de contrato ou comportamento;
4. `otelcol`, LGTM, dashboard e alertas continuam exatamente como estao hoje;
5. o gerador de carga entra apenas como utilitario host-side para automatizar a
   emissao de requests reais contra o endpoint existente.

Arquitetura alvo da implementacao:

1. adicionar um unico entrypoint versionado em `tools/load-generator/`;
2. receber parametros simples de operacao no host, com defaults seguros para a
   demo local;
3. montar payloads reais compativeis com `CreateOrderRequest` do
   `OrderService`;
4. executar requests em dois modos controlados: `happy` e `latency`;
5. imprimir resumo simples e observavel no terminal, sem gerar artefatos,
   banco, topicos, containers ou dashboards extras;
6. ser referenciado pelo `README.md`, que continua como roteiro canonico da
   demonstracao.

Fronteira tecnica obrigatoria:

1. nenhum arquivo em `src/`, `grafana/`, `processors/`, `otelcol.yaml` ou
   `docker-compose.yaml` precisa mudar para a feature existir;
2. o utilitario opera apenas contra `http://localhost:8080/orders` ou
   `BaseUrl` equivalente fornecido pela pessoa usuaria;
3. a feature nao tenta medir performance cientificamente; ela apenas gera carga
   reproduzivel o bastante para apoiar a demo operacional.

---

## Code Reuse Analysis

### Existing Components to Leverage

| Component | Location | How to Use |
| --- | --- | --- |
| Contrato HTTP de criacao de pedido | `src/OrderService/Program.cs` | Reutilizar o endpoint real `POST /orders`, sem endpoint auxiliar nem flag especial |
| Modelo de request | `src/OrderService/Contracts/CreateOrderRequest.cs` | Reutilizar o payload minimo com o campo `description` |
| Roteiro canonico da demo | `README.md` | Referenciar o gerador como comando de apoio, sem criar documentacao paralela |
| Evidencia de ambiente validado | `.specs/project/STATE.md` | Reutilizar a decisao de PowerShell host-side e as validacoes recentes de latencia |
| Padrao de tool helper no repositorio | `tools/alert-webhook-mock/server.py` | Reaproveitar a estrategia de manter utilitarios de demonstracao fora de `src/` |

### Integration Points

| System | Integration Method |
| --- | --- |
| `order-service` | Chamadas HTTP reais para `POST /orders` com JSON `{"description":"..."}` |
| `README.md` | Um unico comando principal apontando para o script em `tools/load-generator/` |
| Alerta `OrderService P95 > 500 ms` | Modo `latency` aumenta a densidade de requests para apoiar a observacao do sinal existente |
| Dashboard e Explore no Grafana | A carga gerada alimenta traces, metricas e logs ja provisionados na baseline |

---

## Proposed File Layout

Estrutura minima esperada para a implementacao:

```text
tools/
  load-generator/
    generate-orders.ps1
```

Escopo documental esperado fora do script:

```text
README.md
```

O design nao preve modulo PowerShell separado, manifest, container dedicado ou
arquivo de configuracao externo para o MVP. O objetivo e manter a implementacao
pequena, direta e verificavel.

---

## Components

### Entry Script

- **Purpose**: ser o unico ponto canonico de execucao do gerador de carga no host.
- **Location**: `tools/load-generator/generate-orders.ps1`
- **Interfaces**:
  - `param([string]$BaseUrl = "http://localhost:8080")` - define o host base do `order-service`
  - `param([int]$Count)` - define quantas requests totais enviar
  - `param([ValidateSet("happy","latency")][string]$Mode = "happy")` - escolhe a estrategia de emissao
  - `param([int]$Concurrency = 1)` - controla quantos workers simultaneos usar no modo `latency`
  - `param([int]$TimeoutSeconds = 10)` - define timeout por request
  - `param([int]$PauseMs = 0)` - pausa opcional entre requests ou batches, com uso principal em tuning manual da demo
- **Dependencies**: PowerShell host-side, `Invoke-WebRequest` ou `Invoke-RestMethod`, endpoint `POST /orders` disponivel
- **Reuses**: endpoint e payload reais do `OrderService`, padrao de comandos PowerShell ja validado no host

### Payload Builder

- **Purpose**: construir payloads validos e unicos para cada request, sem depender de arquivo externo.
- **Location**: funcoes internas em `tools/load-generator/generate-orders.ps1`
- **Interfaces**:
  - `New-OrderRequestBody([string]$Mode, [int]$Sequence): string` - gera JSON compacto com `description` unica
- **Dependencies**: geracao de GUID e serializacao JSON nativa do PowerShell
- **Reuses**: contrato `CreateOrderRequest(string? Description)` ja existente no `OrderService`

### Execution Strategy

- **Purpose**: aplicar o modo de envio escolhido e isolar a diferenca entre demo feliz e pressao de latencia.
- **Location**: funcoes internas em `tools/load-generator/generate-orders.ps1`
- **Interfaces**:
  - `Invoke-HappyPathLoad(...)` - envia requests sequenciais ou com baixa densidade, priorizando leitura e estabilidade da demo
  - `Invoke-LatencyLoad(...)` - envia requests em batches com concorrencia opcional para aumentar a pressao do lado cliente
  - `Invoke-OrderRequest(...)` - executa uma unica chamada HTTP e devolve o resultado normalizado
- **Dependencies**: endpoint HTTP saudavel, parametros validados, cronometro local por request
- **Reuses**: padrao de medicao simples ja usado nas validacoes recentes com `Stopwatch`

### Summary Reporter

- **Purpose**: consolidar o resultado da execucao no terminal sem gerar artefato externo.
- **Location**: funcoes internas em `tools/load-generator/generate-orders.ps1`
- **Interfaces**:
  - `Write-LoadSummary([object[]]$Results)` - imprime total, sucessos, falhas, tempo total e resumo simples de latencia
  - `Write-LoadFailure([object]$Result)` - destaca falhas sem interromper o resumo agregado
- **Dependencies**: colecao de resultados em memoria durante a execucao
- **Reuses**: foco operacional da demo descrito no `README.md`

---

## Parameter Contract

Contrato minimo do script para o MVP:

| Parameter | Required | Default | Purpose |
| --- | --- | --- | --- |
| `BaseUrl` | Nao | `http://localhost:8080` | Permite apontar para o `order-service` exposto no host |
| `Count` | Sim | sem default | Define o total de requests que o utilitario deve tentar enviar |
| `Mode` | Nao | `happy` | Alterna entre carga de fluxo feliz e carga voltada a pressionar latencia |
| `Concurrency` | Nao | `1` | Define paralelismo client-side no modo `latency` |
| `TimeoutSeconds` | Nao | `10` | Limita quanto tempo uma request individual pode levar |
| `PauseMs` | Nao | `0` | Permite cadenciar requests ou batches sem alterar a aplicacao |

Regras de validacao esperadas:

1. `Count` deve ser maior que zero;
2. `Concurrency` deve ser maior que zero;
3. `TimeoutSeconds` deve ser maior que zero;
4. `PauseMs` nao pode ser negativo;
5. `Mode = happy` ignora qualquer tuning de alta densidade que nao seja
   necessario para a demo basica.

---

## Execution Model

### Modo `happy`

Objetivo operacional:

1. popular rapidamente traces, metricas, logs e dashboard sem tornar o terminal
   dificil de ler;
2. privilegiar estabilidade e observabilidade imediata do fluxo ponta a ponta;
3. manter comportamento simples o bastante para ser o comando principal do
   `README.md`.

Comportamento esperado:

1. requests enviadas de forma sequencial;
2. uma `description` unica por request no formato
   `happy-<guid>` ou equivalente;
3. captura do `orderId` quando a resposta for bem-sucedida;
4. resumo final com total, sucessos, falhas e estatisticas simples de duracao.

### Modo `latency`

Objetivo operacional:

1. aumentar a densidade de requests para apoiar a observacao do alerta de
   latencia ja provisionado;
2. continuar usando apenas o endpoint real do `order-service`;
3. manter a implementacao pequena e compativel com PowerShell host-side.

Comportamento esperado:

1. distribuir as requests em batches controlados por `Concurrency`;
2. executar cada batch com paralelismo client-side simples, sem exigir modulo
   externo nem container auxiliar;
3. aceitar que a medicao local reflita tambem overhead do proprio PowerShell,
   porque a meta e demonstracao operacional, nao benchmark preciso;
4. permitir `PauseMs` entre batches apenas como ajuste manual de demo, nao como
   dependencia do sinal do servidor.

Direcao de implementacao recomendada:

1. usar jobs nativos do PowerShell para concorrencia apenas quando
   `Mode = latency` e `Concurrency > 1`;
2. manter o caminho `happy` totalmente sequencial e direto;
3. manter o resultado de cada request normalizado antes de agregacao.

---

## Data Models

### Request Payload

```text
{
  description: string
}
```

Origem:

1. o campo segue exatamente o contrato de `CreateOrderRequest`;
2. o valor deve ser unico por request para facilitar a leitura operacional em
   logs, traces e consultas manuais.

### Per-Request Result

```text
{
  sequence: int,
  mode: string,
  description: string,
  succeeded: bool,
  statusCode: int?,
  durationMs: int,
  orderId: guid?,
  error: string?
}
```

Uso:

1. estruturar o resumo final sem depender de parsing de console textual;
2. permitir diferenciar sucesso HTTP, falha HTTP e falha de transporte.

### Execution Summary

```text
{
  mode: string,
  requested: int,
  succeeded: int,
  failed: int,
  totalDurationMs: int,
  minDurationMs: int?,
  avgDurationMs: int?,
  maxDurationMs: int?
}
```

Uso:

1. imprimir saida final curta e compreensivel no terminal;
2. apoiar leitura manual durante a demo;
3. evitar arquivos de output, CSV ou persistencia local.

---

## Console Output Design

Saida esperada por request, de forma sucinta:

```text
[3/20] status=201 ms=142 orderId=...
```

Saida esperada no resumo final:

```text
mode=happy requested=20 succeeded=20 failed=0 totalMs=3021 avgMs=151 minMs=102 maxMs=244
```

Regras de saida:

1. sucesso e falha devem ser visiveis sem verbose excessivo;
2. o resumo final deve existir mesmo com falhas parciais;
3. o script deve retornar exit code diferente de zero quando nenhuma request for
   concluida com sucesso ou quando todos os envios falharem por indisponibilidade
   do endpoint;
4. falhas parciais podem retornar exit code nao zero se a implementacao futura
   optar por tratar qualquer falha como erro operacional da execucao.

---

## Error Handling Strategy

| Error Scenario | Handling | User Impact |
| --- | --- | --- |
| `Count <= 0` | falha rapida na validacao de parametros antes de enviar requests | erro imediato e nenhum side effect |
| `BaseUrl` invalido | falha rapida com mensagem objetiva | correcao local simples |
| `POST /orders` retorna `400` ou `500` | registrar falha por request e seguir a execucao | resumo final mostra degradacao parcial |
| `POST /orders` retorna `503` | registrar falha e manter agregacao final | ajuda a diagnosticar indisponibilidade do Kafka ou publish |
| timeout de request | classificar como falha de transporte e seguir para o resumo | operador entende que a carga nao completou integralmente |
| excecao em job concorrente | normalizar erro para o resultado da request ou do batch | modo `latency` continua observavel e depuravel |

---

## Tech Decisions

| Decision | Choice | Rationale |
| --- | --- | --- |
| Entry point | script PowerShell unico em `tools/load-generator/generate-orders.ps1` | menor superficie de manutencao para um helper de demo |
| Cliente HTTP | cmdlets nativos do PowerShell | zero dependencia extra no host validado |
| Contrato do payload | apenas `description` | corresponde exatamente ao endpoint atual do `OrderService` |
| Estrategia `happy` | sequencial | leitura simples e execucao previsivel no README |
| Estrategia `latency` | batches com concorrencia opcional | aumenta pressao sem exigir benchmark tool externa |
| Persistencia do resultado | nenhuma | a feature precisa apenas de output de terminal e sinais da PoC |
| Documentacao | referencia curta no `README.md` | evita duplicar walkthrough da demo |

---

## Implementation Boundaries

Mudancas esperadas na etapa de implementacao:

1. criar `tools/load-generator/generate-orders.ps1`;
2. atualizar o `README.md` apenas o suficiente para apontar para o comando
   principal do gerador e para o modo opcional de latencia;
3. nao tocar servicos .NET, collector, compose funcional, dashboards, alertas,
   contratos Kafka, payloads ou persistencia.

Mudancas explicitamente proibidas:

1. adicionar container dedicado de load generator ao compose;
2. criar endpoint especial de benchmark no `OrderService`;
3. introduzir metricas novas para acompanhar o utilitario;
4. publicar novas portas no host;
5. transformar o utilitario em roteiro alternativo ao `README.md`.

---

## Validation Intent For Future Tasks

O design considera suficiente, para a proxima etapa de tasks e futura
implementacao, que a validacao consiga:

1. executar o script em modo `happy` e gerar N pedidos validos contra
   `http://localhost:8080/orders`;
2. comprovar que a carga produz atividade observavel em traces, metricas, logs e
   dashboard sem qualquer mudanca funcional na aplicacao;
3. executar o script em modo `latency` com densidade maior de requests e usar o
   resultado para apoiar a navegacao do alerta `OrderService P95 > 500 ms`;
4. demonstrar que o `README.md` continua sendo o roteiro canonico, com o
   gerador apenas como helper versionado de execucao.