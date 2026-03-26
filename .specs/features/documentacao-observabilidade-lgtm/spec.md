# Feature: Documentacao Observabilidade & LGTM Stack

## Escopo

**Medium-Large** — ~10 documentos Markdown em `/docs`, conteudo educacional do basico ao avancado.

## Objetivo

Criar uma suite de documentacao em `docs/` que ensine Observabilidade e a stack LGTM (Loki, Grafana, Tempo, Mimir/Prometheus) de forma organizada, concisa e progressiva. Cada documento usa exemplos reais desta PoC, compara com melhores praticas de mercado e inclui referencias externas.

## Requisitos

### REQ-01: Estrutura da pasta docs/
- Criar `docs/` na raiz do repositorio
- Indice principal (`README.md`) com navegacao entre documentos
- Organizacao por pilar de observabilidade + stack

### REQ-02: Fundamentos de Observabilidade
- Tres pilares: metricas, traces, logs
- Por que observabilidade != monitoramento
- Conceitos de SLI, SLO, SLA
- Golden Signals (latencia, trafego, erros, saturacao)
- Referencias: Google SRE Book, OpenTelemetry docs

### REQ-03: Metricas
- Tipos de metricas: Counter, Gauge, Histogram, Summary
- Quando usar cada tipo (com exemplos da PoC)
- Nomenclatura e convencoes (Prometheus naming)
- Como analisar metricas: rate(), histogram_quantile(), increase()
- Exemplos PromQL com metricas da PoC
- Comparacao: metricas da PoC vs melhores praticas
- Referencias

### REQ-04: Distributed Tracing
- Conceitos: span, trace, context propagation
- W3C TraceContext (traceparent, tracestate)
- Propagacao em sistemas assincronos (Kafka)
- Como a PoC propaga traces entre servicos
- Analise de traces: latencia, dependencias, erros
- Sampling strategies (head vs tail)
- Exemplos com Tempo
- Referencias

### REQ-05: Logging Estruturado
- Logs estruturados vs texto livre
- Correlacao logs-traces (TraceId, SpanId)
- Niveis de log e quando usar cada um
- Como a PoC exporta logs via OTLP para Loki
- Consultas LogQL no Loki
- Melhores praticas de logging
- Referencias

### REQ-06: OpenTelemetry
- Arquitetura do OTel (SDK, API, Collector)
- Instrumentacao em .NET (auto vs manual)
- Configuracao do OTel Collector (receivers, processors, exporters, pipelines)
- Tail Sampling no Collector
- Exemplars: o que sao, como habilitar, como usar
- Como a PoC configura OTel em cada servico
- Referencias

### REQ-07: Stack LGTM
- Visao geral: Loki + Grafana + Tempo + Mimir (Prometheus)
- Papel de cada componente
- Arquitetura de deployment (all-in-one vs microservices mode)
- Como a PoC usa a imagem grafana/otel-lgtm
- Alternativas e comparacoes (ELK, Jaeger, Datadog)
- Referencias

### REQ-08: Grafana — Dashboards e Alertas
- Criacao de dashboards (panels, queries, variables)
- Dashboard as Code (provisioning)
- Alerting: regras, contact points, notification policies
- Como a PoC provisiona dashboard e alertas
- Exemplars no Grafana (metricas → traces)
- Melhores praticas de dashboarding
- Referencias

### REQ-09: Padroes Arquiteturais de Observabilidade
- Transaction Outbox + CDC e rastreabilidade
- Kafka consumer lag como metrica de saude
- Correlation IDs em event-driven architecture
- Health checks e readiness probes
- Observabilidade como codigo (IaC)
- Referencias

### REQ-10: Guia Pratico / Quick Start
- Como subir a stack localmente
- Como gerar carga e visualizar dados
- Checklist de observabilidade para novos servicos
- Troubleshooting comum

## Restricoes

- Idioma: Portugues (BR)
- Formato: Markdown (GitHub-flavored)
- Cada doc deve ser auto-contido mas referenciar os outros quando necessario
- Conciso: preferir tabelas, diagramas e exemplos a paredes de texto
- Incluir bloco de referencias ao final de cada documento
