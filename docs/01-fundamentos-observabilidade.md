# Fundamentos de Observabilidade

## Sumario

1. [O que e Observabilidade](#o-que-e-observabilidade)
2. [Os 3 Pilares](#os-3-pilares)
3. [Golden Signals](#golden-signals)
4. [SLI, SLO e SLA](#sli-slo-e-sla)
5. [Por que Observabilidade importa em Microservicos](#por-que-observabilidade-importa-em-microservicos)
6. [Referencias](#referencias)

---

## O que e Observabilidade

**Definicao formal:** Observabilidade e a capacidade de inferir o estado interno de um sistema a partir de suas saidas externas (metricas, traces, logs). O termo vem da teoria de controle: um sistema e "observavel" quando seu estado interno pode ser determinado inteiramente pelas suas saidas.

### Observabilidade vs. Monitoramento

| Aspecto | Monitoramento | Observabilidade |
|---------|--------------|-----------------|
| Abordagem | Reativa: "o que eu ja sei que pode quebrar?" | Exploratoria: "o que esta acontecendo agora?" |
| Perguntas | Conhecidas antecipadamente (dashboards pre-definidos) | Desconhecidas -- investigacao ad hoc |
| Foco | Alertas sobre falhas conhecidas | Entender comportamento emergente |
| Metafora | Painel de carro com luzes de alerta | Raio-X completo do motor |

Monitoramento e um **subconjunto** de observabilidade. Voce monitora o que ja conhece; observabilidade permite diagnosticar o que voce **nunca previu**.

---

## Os 3 Pilares

Os tres sinais fundamentais que tornam um sistema observavel:

### Metricas

**O que sao:** Valores numericos agregados ao longo do tempo (time series). Representam contagens, taxas, duracoes e estados.

**Analogia:** O velocimetro e o conta-giros do carro. Voce nao sabe *por que* a velocidade caiu, mas sabe *que* caiu e *quando*.

**Exemplo na PoC:** `orders.created.total` conta quantos pedidos foram criados; `orders.create.duration` mede quanto tempo cada criacao levou.

### Traces (Rastreamento Distribuido)

**O que sao:** Representacao do caminho completo de uma requisicao atraves de multiplos servicos, composta por *spans* (unidades de trabalho) conectados por relacao pai-filho.

**Analogia:** O rastreamento de uma encomenda pelos Correios. Cada carimbo (span) registra onde a encomenda passou, quanto tempo ficou em cada etapa e se houve algum problema.

**Exemplo na PoC:** Uma requisicao POST /orders gera um trace que passa por OrderService -> Kafka (via Outbox+Debezium) -> ProcessingWorker -> Kafka -> NotificationWorker, tudo conectado pelo mesmo `trace_id`.

### Logs

**O que sao:** Registros textuais (preferencialmente estruturados) de eventos discretos que acontecem no sistema, com timestamp e contexto.

**Analogia:** O diario de bordo de um navio. Cada entrada registra um evento especifico com detalhes do que aconteceu.

**Exemplo na PoC:** Logs estruturados via `ILogger` exportados via OTLP gRPC para o Loki, incluindo `TraceId` e `SpanId` para correlacao com traces no Tempo.

### Correlacao entre os Pilares

A verdadeira forca da observabilidade esta na **correlacao**:

```
Metrica (alerta: latencia P95 subiu)
  -> Exemplar (link para um trace_id especifico)
    -> Trace (mostra que o span do banco de dados esta lento)
      -> Log (SQL query com full table scan)
```

Na PoC, essa correlacao e viabilizada por **Exemplars** (metricas -> traces) e pela inclusao de `TraceId`/`SpanId` nos logs (logs -> traces).

---

## Golden Signals

Os quatro sinais dourados definidos pelo livro SRE do Google sao as metricas mais importantes para qualquer servico orientado a usuario:

| Signal | Definicao | Exemplo na PoC |
|--------|-----------|-----------------|
| **Latencia** | Tempo para atender uma requisicao | `orders.create.duration` -- histogram que mede o tempo de criacao de pedidos em ms. P95 e P99 sao os percentis mais relevantes. |
| **Trafego** | Volume de demanda no sistema | `rate(orders_created_total[5m])` -- taxa de pedidos criados por segundo nos ultimos 5 minutos. |
| **Erros** | Taxa de requisicoes que falham | `rate(orders_created_total{result!="created"}[5m])` -- pedidos que falharam (validacao, persistencia). |
| **Saturacao** | O quao "cheio" o sistema esta | `orders.backlog.current` -- pedidos pendentes no outbox; `kafka.consumer.lag` -- mensagens nao consumidas no Kafka. |

### Por que esses quatro?

- **Latencia** e o que o usuario sente diretamente.
- **Trafego** indica a carga e ajuda a dimensionar capacidade.
- **Erros** revelam problemas funcionais.
- **Saturacao** antecipa problemas *antes* que afetem latencia e erros.

Juntos, eles cobrem tanto sintomas (latencia, erros) quanto causas (saturacao, trafego).

---

## SLI, SLO e SLA

### Definicoes

| Conceito | Significado | O que define |
|----------|-------------|-------------|
| **SLI** (Service Level Indicator) | Uma metrica quantitativa do comportamento do servico | *O que* voce mede |
| **SLO** (Service Level Objective) | Um alvo para o SLI | *Qual nivel* voce quer atingir |
| **SLA** (Service Level Agreement) | Um contrato formal com consequencias caso o SLO seja violado | *O que acontece* se voce falhar |

### Relacao entre eles

```
SLI  -->  "99.2% das requisicoes com latencia < 500ms"  (medicao real)
SLO  -->  "99.5% das requisicoes devem ter latencia < 500ms"  (objetivo interno)
SLA  -->  "99% das requisicoes devem ter latencia < 500ms, senao credito de 10%"  (contrato)
```

O SLA e sempre **mais permissivo** que o SLO. O SLO serve como "colchao" antes de violar o SLA.

### Exemplo pratico com a PoC

| Camada | Exemplo |
|--------|---------|
| **SLI** | `histogram_quantile(0.99, sum by (le) (rate(orders_create_duration_milliseconds_bucket{result="created"}[5m])))` = 380ms |
| **SLO** | P99 da latencia de criacao de pedidos deve ser < 500ms em 99.5% do tempo (janela de 30 dias) |
| **SLA** | P99 da latencia de criacao de pedidos deve ser < 1s em 99% do tempo. Violacao gera credito proporcional. |

### Error Budget

O **error budget** e o complemento do SLO: se o SLO e 99.5%, voce tem 0.5% de "orcamento de erros". Enquanto estiver dentro do budget, o time pode priorizar features; quando esgotar, prioriza confiabilidade.

---

## Por que Observabilidade importa em Microservicos

### A complexidade distribuida

Em uma arquitetura monolitica, um problema geralmente esta em um unico processo. Em microservicos:

- Uma requisicao atravessa **multiplos servicos** (na PoC: OrderService -> Kafka -> ProcessingWorker -> Kafka -> NotificationWorker).
- Cada servico tem seu **proprio banco de dados** e **proprio ciclo de deploy**.
- Falhas podem ser **transitivas**: o ProcessingWorker falha porque o OrderService esta lento em responder a chamada HTTP de enriquecimento.
- Comunicacao **assincrona** (Kafka) adiciona latencia e pontos de falha invisiveis.

### Problemas que observabilidade resolve

| Problema | Sem observabilidade | Com observabilidade |
|----------|-------------------|-------------------|
| "O pedido sumiu" | Verificar logs de cada servico manualmente | Buscar pelo `trace_id` no Tempo e ver todos os spans |
| "A API esta lenta" | Reiniciar servicos e torcer | Ver o histogram P95, identificar o span lento via exemplar |
| "Kafka esta acumulando" | Descobrir so quando o usuario reclama | Alerta automatico via `kafka.consumer.lag` |
| "Qual servico causou o erro?" | Reuniao com 3 times | Trace mostra exatamente onde o status mudou para ERROR |

### O custo de nao ter observabilidade

- **MTTR (Mean Time To Recovery)** aumenta drasticamente.
- **Debugging** se torna "adivinhar e reiniciar".
- **Incidentes recorrentes** porque a causa raiz nunca e encontrada.
- **Confianca no deploy** cai -- times evitam deploys por medo de quebrar algo invisivel.

---

## Referencias

### Livros e Documentacao Oficial

- **Google SRE Book (gratis online):** [https://sre.google/sre-book/table-of-contents/](https://sre.google/sre-book/table-of-contents/)
  - Capitulo 6: Monitoring Distributed Systems (Golden Signals)
  - Capitulo 4: Service Level Objectives
- **Google SRE Workbook (gratis online):** [https://sre.google/workbook/table-of-contents/](https://sre.google/workbook/table-of-contents/)
- **OpenTelemetry Documentation:** [https://opentelemetry.io/docs/](https://opentelemetry.io/docs/)
  - Conceitos: [https://opentelemetry.io/docs/concepts/](https://opentelemetry.io/docs/concepts/)
  - .NET SDK: [https://opentelemetry.io/docs/languages/dotnet/](https://opentelemetry.io/docs/languages/dotnet/)

### Artigos e Blog Posts

- **Charity Majors -- Observability:**
  - "Observability -- A 3-Year Retrospective": [https://charity.wtf/2022/07/14/observability-a-3-year-retrospective/](https://charity.wtf/2022/07/14/observability-a-3-year-retrospective/)
  - "Observability is a Many-Splendored Thing": [https://charity.wtf/2020/03/03/observability-is-a-many-splendored-thing/](https://charity.wtf/2020/03/03/observability-is-a-many-splendored-thing/)
  - "Logs vs Structured Events": [https://charity.wtf/2019/02/05/logs-vs-structured-events/](https://charity.wtf/2019/02/05/logs-vs-structured-events/)
- **Cindy Sridharan -- Distributed Systems Observability (gratis):** [https://www.oreilly.com/library/view/distributed-systems-observability/9781492033431/](https://www.oreilly.com/library/view/distributed-systems-observability/9781492033431/)

### Especificacoes

- **W3C Trace Context:** [https://www.w3.org/TR/trace-context/](https://www.w3.org/TR/trace-context/)
- **OpenTelemetry Specification:** [https://opentelemetry.io/docs/specs/otel/](https://opentelemetry.io/docs/specs/otel/)
